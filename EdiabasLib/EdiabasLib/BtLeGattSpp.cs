﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Android.Bluetooth;
using Android.Content;
using Android.OS;

namespace EdiabasLib
{
    public class BtLeGattSpp : IDisposable
    {
        public delegate void LogStringDelegate(string message);

#if DEBUG
        private static readonly string Tag = typeof(BtLeGattSpp).FullName;
#endif
        private static readonly Java.Util.UUID GattServiceCarlySpp = Java.Util.UUID.FromString("0000ffe0-0000-1000-8000-00805f9b34fb");
        private static readonly Java.Util.UUID GattCharacteristicCarlySpp = Java.Util.UUID.FromString("0000ffe1-0000-1000-8000-00805f9b34fb");
        private static readonly Java.Util.UUID GattServiceWgSoftSpp = Java.Util.UUID.FromString("0000fff0-0000-1000-8000-00805f9b34fb");
        private static readonly Java.Util.UUID GattCharacteristicWgSoftSppRead = Java.Util.UUID.FromString("0000fff1-0000-1000-8000-00805f9b34fb");
        private static readonly Java.Util.UUID GattCharacteristicWgSoftSppWrite = Java.Util.UUID.FromString("0000fff2-0000-1000-8000-00805f9b34fb");
        private static readonly Java.Util.UUID GattCharacteristicConfig = Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb");

        private bool _disposed;
        private readonly LogStringDelegate _logStringHandler;
        private readonly AutoResetEvent _btGattConnectEvent = new AutoResetEvent(false);
        private readonly AutoResetEvent _btGattDiscoveredEvent = new AutoResetEvent(false);
        private readonly AutoResetEvent _btGattReceivedEvent = new AutoResetEvent(false);
        private readonly AutoResetEvent _btGattWriteEvent = new AutoResetEvent(false);
        private BluetoothGatt _bluetoothGatt;
        private BluetoothGattCharacteristic _gattCharacteristicSppRead;
        private BluetoothGattCharacteristic _gattCharacteristicSppWrite;
        private Java.Util.UUID _gattCharacteristicUuidSppRead;
        private Java.Util.UUID _gattCharacteristicUuidSppWrite;
        private volatile State _gattConnectionState = State.Disconnected;
        private volatile bool _gattServicesDiscovered;
        private GattStatus _gattWriteStatus = GattStatus.Failure;
        private MemoryQueueBufferStream _btGattSppInStream;
        private BGattOutputStream _btGattSppOutStream;

        public State GattConnectionState => _gattConnectionState;
        public bool GattServicesDiscovered => _gattServicesDiscovered;
        public MemoryQueueBufferStream BtGattSppInStream => _btGattSppInStream;
        public BGattOutputStream BtGattSppOutStream => _btGattSppOutStream;

        public BtLeGattSpp(LogStringDelegate logStringHandler = null)
        {
            _logStringHandler = logStringHandler;
        }

        public void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SupressFinalize to
            // take this object off the finalization queue
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!_disposed)
            {
                // If disposing equals true, dispose all managed
                // and unmanaged resources.
                if (disposing)
                {
                    BtGattDisconnect();
                }

                // Note disposing has been done.
                _disposed = true;
            }
        }

        public bool ConnectLeGattDevice(Context context, BluetoothDevice device)
        {
            try
            {
                BtGattDisconnect();

                _gattConnectionState = State.Connecting;
                _gattServicesDiscovered = false;
                _btGattSppInStream = new MemoryQueueBufferStream(true);
                _btGattSppOutStream = new BGattOutputStream(this);
                BGattBaseCallback bGattCallback = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu ? new BGatt2Callback(this) : new BGatt1Callback(this);
                _bluetoothGatt = device.ConnectGatt(context, false, bGattCallback, BluetoothTransports.Le);
                if (_bluetoothGatt == null)
                {
                    LogString("*** ConnectGatt failed");
                    return false;
                }

                _btGattConnectEvent.WaitOne(3000, false);
                if (_gattConnectionState != State.Connected)
                {
                    LogString("*** GATT connection timeout");
                    return false;
                }

                _btGattDiscoveredEvent.WaitOne(2000, false);
                if (!_gattServicesDiscovered)
                {
                    LogString("*** GATT service discovery timeout");
                    return false;
                }

                IList<BluetoothGattService> services = _bluetoothGatt.Services;
                if (services == null)
                {
                    LogString("*** No GATT services found");
                    return false;
                }

#if DEBUG
                foreach (BluetoothGattService gattService in services)
                {
                    if (gattService.Uuid == null || gattService.Characteristics == null)
                    {
                        continue;
                    }

                    Android.Util.Log.Info(Tag, string.Format("GATT service: {0}", gattService.Uuid));
                    foreach (BluetoothGattCharacteristic gattCharacteristic in gattService.Characteristics)
                    {
                        if (gattCharacteristic.Uuid == null)
                        {
                            continue;
                        }

                        Android.Util.Log.Info(Tag, string.Format("GATT characteristic: {0}", gattCharacteristic.Uuid));
                        Android.Util.Log.Info(Tag, string.Format("GATT properties: {0}", gattCharacteristic.Properties));
                    }
                }
#endif

                _gattCharacteristicSppRead = null;
                _gattCharacteristicSppWrite = null;
                _gattCharacteristicUuidSppRead = null;
                _gattCharacteristicUuidSppWrite = null;

                BluetoothGattService gattServiceSpp = _bluetoothGatt.GetService(GattServiceCarlySpp);
                BluetoothGattCharacteristic gattCharacteristicSpp = gattServiceSpp?.GetCharacteristic(GattCharacteristicCarlySpp);
                if (gattCharacteristicSpp != null)
                {
                    if ((gattCharacteristicSpp.Properties & (GattProperty.Read | GattProperty.Write | GattProperty.Notify)) ==
                        (GattProperty.Read | GattProperty.Write | GattProperty.Notify))
                    {
                        _gattCharacteristicSppRead = gattCharacteristicSpp;
                        _gattCharacteristicSppWrite = gattCharacteristicSpp;
                        _gattCharacteristicUuidSppRead = GattCharacteristicCarlySpp;
                        _gattCharacteristicUuidSppWrite = GattCharacteristicCarlySpp;
#if DEBUG
                        Android.Util.Log.Info(Tag, "SPP characteristic Carly found");
#endif
                    }
                }
                else
                {
                    gattServiceSpp = _bluetoothGatt.GetService(GattServiceWgSoftSpp);
                    BluetoothGattCharacteristic gattCharacteristicSppRead = gattServiceSpp?.GetCharacteristic(GattCharacteristicWgSoftSppRead);
                    BluetoothGattCharacteristic gattCharacteristicSppWrite = gattServiceSpp?.GetCharacteristic(GattCharacteristicWgSoftSppWrite);
                    if (gattCharacteristicSppRead != null && gattCharacteristicSppWrite != null)
                    {
                        if (((gattCharacteristicSppRead.Properties & (GattProperty.Read | GattProperty.Notify)) == (GattProperty.Read | GattProperty.Notify)) &&
                            ((gattCharacteristicSppWrite.Properties & (GattProperty.Write)) == (GattProperty.Write)))
                        {
                            _gattCharacteristicSppRead = gattCharacteristicSppRead;
                            _gattCharacteristicSppWrite = gattCharacteristicSppWrite;
                            _gattCharacteristicUuidSppRead = GattCharacteristicWgSoftSppRead;
                            _gattCharacteristicUuidSppWrite = GattCharacteristicWgSoftSppWrite;
                        }
#if DEBUG
                        Android.Util.Log.Info(Tag, "SPP characteristic WgSoft found");
#endif
                    }
                }

                if (_gattCharacteristicSppRead == null || _gattCharacteristicSppWrite == null)
                {
                    LogString("*** No GATT SPP characteristic found");
                    return false;
                }

                if (!_bluetoothGatt.SetCharacteristicNotification(_gattCharacteristicSppRead, true))
                {
                    LogString("*** GATT SPP enable notification failed");
                    return false;
                }

                BluetoothGattDescriptor descriptor = _gattCharacteristicSppRead.GetDescriptor(GattCharacteristicConfig);
                if (descriptor == null)
                {
                    LogString("*** GATT SPP config descriptor not found");
                    return false;
                }

                if (BluetoothGattDescriptor.EnableNotificationValue == null)
                {
                    LogString("*** GATT SPP EnableNotificationValue not present");
                    return false;
                }

                _gattWriteStatus = GattStatus.Failure;
                byte[] enableNotifyArray = BluetoothGattDescriptor.EnableNotificationValue.ToArray();
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    if (_bluetoothGatt.WriteDescriptor(descriptor, enableNotifyArray) != (int) CurrentBluetoothStatusCodes.Success)
                    {
                        LogString("*** GATT SPP write config descriptor failed");
                        return false;
                    }
                }
                else
                {
#pragma warning disable CS0618
                    descriptor.SetValue(enableNotifyArray);
                    if (!_bluetoothGatt.WriteDescriptor(descriptor))
#pragma warning restore CS0618
                    {
                        LogString("*** GATT SPP write config descriptor failed");
                        return false;
                    }
                }

                if (!_btGattWriteEvent.WaitOne(2000))
                {
                    LogString("*** GATT SPP write config descriptor timeout");
                    return false;
                }

                if (_gattWriteStatus != GattStatus.Success)
                {
                    LogString("*** GATT SPP write config descriptor status failure");
                    return false;
                }

#if false
                byte[] sendData = Encoding.UTF8.GetBytes("ATI\r");
                _btGattSppOutStream.Write(sendData, 0, sendData.Length);

                while (_btGattReceivedEvent.WaitOne(2000, false))
                {
#if DEBUG
                    Android.Util.Log.Info(Tag, "GATT SPP data received");
#endif
                }

                while (_btGattSppInStream.HasData())
                {
                    int data = _btGattSppInStream.ReadByteAsync();
                    if (data < 0)
                    {
                        break;
                    }
#if DEBUG
                    Android.Util.Log.Info(Tag, string.Format("GATT SPP byte: {0:X02}", data));
#endif
                }
#endif
                return true;
            }
            catch (Exception)
            {
                _gattConnectionState = State.Disconnected;
                _gattServicesDiscovered = false;
                return false;
            }
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private bool ReceiveGattSppData(BluetoothGattCharacteristic characteristic, byte[] value = null)
        {
            try
            {
                if (characteristic.Uuid != null && _gattCharacteristicUuidSppRead != null &&
                    characteristic.Uuid.Equals(_gattCharacteristicUuidSppRead))
                {
                    byte[] data = value;
                    if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
                    {
#pragma warning disable CS0618
                        data = characteristic.GetValue();
#pragma warning restore CS0618
                    }

                    if (data != null)
                    {
#if DEBUG
                        Android.Util.Log.Info(Tag, string.Format("GATT SPP data received: {0} '{1}'",
                            BitConverter.ToString(data).Replace("-", ""), Encoding.UTF8.GetString(data)));
#endif
                        _btGattSppInStream?.Write(data);
                        _btGattReceivedEvent.Set();
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        public void BtGattDisconnect()
        {
            try
            {
                if (_gattCharacteristicSppRead != null)
                {
                    try
                    {
                        _bluetoothGatt?.SetCharacteristicNotification(_gattCharacteristicSppRead, false);
                    }
                    catch (Exception)
                    {
                        // ignored
                    }

                    _gattCharacteristicSppRead = null;
                }

                _gattCharacteristicSppWrite = null;

                _gattConnectionState = State.Disconnected;
                _gattServicesDiscovered = false;

                if (_bluetoothGatt != null)
                {
                    try
                    {
                        _bluetoothGatt.Disconnect();
                    }
                    catch (Exception)
                    {
                        // ignored
                    }

                    _bluetoothGatt.Dispose();
                    _bluetoothGatt = null;
                }

                if (_btGattSppInStream != null)
                {
                    _btGattSppInStream.Dispose();
                    _btGattSppInStream = null;
                }

                if (_btGattSppOutStream != null)
                {
                    _btGattSppOutStream.Dispose();
                    _btGattSppOutStream = null;
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void LogString(string info)
        {
            _logStringHandler?.Invoke(info);
        }

        private class BGattBaseCallback : BluetoothGattCallback
        {
            protected readonly BtLeGattSpp _btLeGattSpp;

            protected BGattBaseCallback(BtLeGattSpp btLeGattSpp)
            {
                _btLeGattSpp = btLeGattSpp;
            }

            public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
            {
                if (gatt != _btLeGattSpp._bluetoothGatt)
                {
                    return;
                }

                if (newState == ProfileState.Connected)
                {
#if DEBUG
                    Android.Util.Log.Info(Tag, "Connected to GATT server.");
#endif
                    _btLeGattSpp._gattConnectionState = State.Connected;
                    _btLeGattSpp._gattServicesDiscovered = false;
                    _btLeGattSpp._btGattConnectEvent.Set();
                    gatt.DiscoverServices();
                }
                else if (newState == ProfileState.Disconnected)
                {
#if DEBUG
                    Android.Util.Log.Info(Tag, "Disconnected from GATT server.");
#endif
                    _btLeGattSpp._gattConnectionState = State.Disconnected;
                    _btLeGattSpp._gattServicesDiscovered = false;
                }
            }

            public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
            {
                if (gatt != _btLeGattSpp._bluetoothGatt)
                {
                    return;
                }

                if (status == GattStatus.Success)
                {
#if DEBUG
                    Android.Util.Log.Info(Tag, "GATT services discovered.");
#endif
                    _btLeGattSpp._gattServicesDiscovered = true;
                    _btLeGattSpp._btGattDiscoveredEvent.Set();
                }
                else
                {
#if DEBUG
                    Android.Util.Log.Info(Tag, string.Format("GATT services discovery failed: {0}", status));
#endif
                }
            }

            public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
            {
                if (gatt != _btLeGattSpp._bluetoothGatt)
                {
                    return;
                }

                GattStatus resultStatus = GattStatus.Failure;
                if (status == GattStatus.Success)
                {
                    if (characteristic.Uuid != null && _btLeGattSpp._gattCharacteristicUuidSppWrite != null &&
                        characteristic.Uuid.Equals(_btLeGattSpp._gattCharacteristicUuidSppWrite))
                    {
                        resultStatus = status;
                    }
                }

                _btLeGattSpp._gattWriteStatus = resultStatus;
                _btLeGattSpp._btGattWriteEvent.Set();
            }

            public override void OnDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, GattStatus status)
            {
                if (gatt != _btLeGattSpp._bluetoothGatt)
                {
                    return;
                }

                _btLeGattSpp._gattWriteStatus = status;
                _btLeGattSpp._btGattWriteEvent.Set();
            }
        }

        private class BGatt1Callback : BGattBaseCallback
        {
            public BGatt1Callback(BtLeGattSpp btLeGattSpp) : base(btLeGattSpp)
            {
            }

#pragma warning disable CS0672
            public override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
#pragma warning restore CS0672
            {
                if (gatt != _btLeGattSpp._bluetoothGatt)
                {
                    return;
                }

                if (status == GattStatus.Success)
                {
                    _btLeGattSpp.ReceiveGattSppData(characteristic);
                }
            }

#pragma warning disable CS0672
            public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
#pragma warning restore CS0672
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    return;
                }

                if (gatt != _btLeGattSpp._bluetoothGatt)
                {
                    return;
                }

                _btLeGattSpp.ReceiveGattSppData(characteristic);
            }
        }

        private class BGatt2Callback : BGattBaseCallback
        {
            public BGatt2Callback(BtLeGattSpp btLeGattSpp) : base(btLeGattSpp)
            {
            }

            public override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value, GattStatus status)
            {
                if (gatt != _btLeGattSpp._bluetoothGatt)
                {
                    return;
                }

                if (status == GattStatus.Success)
                {
                    _btLeGattSpp.ReceiveGattSppData(characteristic, value);
                }
            }

            public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value)
            {
                if (gatt != _btLeGattSpp._bluetoothGatt)
                {
                    return;
                }

                _btLeGattSpp.ReceiveGattSppData(characteristic, value);
            }
        }

        public class BGattOutputStream : MemoryQueueBufferStream
        {
            private const int MaxWriteLength = 20;
            readonly BtLeGattSpp _btLeGattSpp;

            public BGattOutputStream(BtLeGattSpp btLeGattSpp) : base(true)
            {
                _btLeGattSpp = btLeGattSpp;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                base.Write(buffer, offset, count);

                if (_btLeGattSpp._gattConnectionState != State.Connected ||
                    _btLeGattSpp._gattCharacteristicSppWrite == null)
                {
                    throw new IOException("GATT disconnected");
                }

                while (Length > 0)
                {
                    byte[] readBuffer = new byte[MaxWriteLength];
                    int length = Read(readBuffer, 0, readBuffer.Length);
                    if (length <= 0)
                    {
                        throw new IOException("Stream write: read chunk failed");
                    }

                    byte[] sendData = new byte[length];
                    Array.Copy(readBuffer, 0, sendData, 0, length);

#if DEBUG
                    Android.Util.Log.Info(Tag, string.Format("GATT SPP data write: {0} '{1}'",
                        BitConverter.ToString(sendData).Replace("-", ""), Encoding.UTF8.GetString(sendData)));
#endif
                    _btLeGattSpp._gattWriteStatus = GattStatus.Failure;
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                    {
                        if (_btLeGattSpp._bluetoothGatt.WriteCharacteristic(_btLeGattSpp._gattCharacteristicSppWrite, sendData, (int) GattWriteType.Default)
                            != (int)CurrentBluetoothStatusCodes.Success)
                        {
                            throw new IOException("WriteCharacteristic failed");
                        }
                    }
                    else
                    {
#pragma warning disable CS0618
                        _btLeGattSpp._gattCharacteristicSppWrite.SetValue(sendData);
                        if (!_btLeGattSpp._bluetoothGatt.WriteCharacteristic(_btLeGattSpp._gattCharacteristicSppWrite))
#pragma warning restore CS0618
                        {
                            throw new IOException("WriteCharacteristic failed");
                        }
                    }

                    if (!_btLeGattSpp._btGattWriteEvent.WaitOne(2000))
                    {
                        throw new IOException("WriteCharacteristic timeout");
                    }

                    if (_btLeGattSpp._gattWriteStatus != GattStatus.Success)
                    {
                        throw new IOException("WriteCharacteristic status failure");
                    }
                }
            }
        }
    }
}
