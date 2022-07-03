﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BmwDeepObd;
using EdiabasLib;

// ReSharper disable UnusedMemberInSuper.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo

namespace BmwFileReader
{
    public class DetectVehicleBmw
    {
        public delegate bool AbortDelegate();
        public delegate void ProgressDelegate(int percent);

        public AbortDelegate AbortFunc { get; set; }
        public ProgressDelegate ProgressFunc { get; set; }

        public bool Ds2Vehicle { get; private set; }
        public string Vin { get; private set; }
        public string GroupSgdb { get; private set; }
        public string ModelSeries { get; private set; }
        public string Series { get; private set; }
        public string Ds2GroupFiles { get; private set; }
        public string ConstructYear { get; private set; }
        public string ConstructMonth { get; private set; }
        public string ILevelShip { get; private set; }
        public string ILevelCurrent { get; private set; }
        public string ILevelBackup { get; private set; }
        public bool Pin78ConnectRequire { get; private set; }

        private EdiabasNet _ediabas;
        private string _bmwDir;

        public const string AllDs2GroupFiles = "d_0000,d_0008,d_000d,d_0010,d_0011,d_0012,d_motor,d_0013,d_0014,d_0015,d_0016,d_0020,d_0021,d_0022,d_0024,d_0028,d_002c,d_002e,d_0030,d_0032,d_0035,d_0036,d_003b,d_0040,d_0044,d_0045,d_0050,d_0056,d_0057,d_0059,d_005a,d_005b,d_0060,d_0068,d_0069,d_006a,d_006c,d_0070,d_0071,d_0072,d_007f,d_0080,d_0086,d_0099,d_009a,d_009b,d_009c,d_009d,d_009e,d_00a0,d_00a4,d_00a6,d_00a7,d_00ac,d_00b0,d_00b9,d_00bb,d_00c0,d_00c8,d_00cd,d_00d0,d_00da,d_00e0,d_00e8,d_00ed,d_00f0,d_00f5,d_00ff,d_b8_d0,,d_m60_10,d_m60_12,d_spmbt,d_spmft,d_szm,d_zke3bt,d_zke3ft,d_zke3pm,d_zke3sb,d_zke3sd,d_zke_gm,d_zuheiz,d_sitz_f,d_sitz_b,d_0047,d_0048,d_00ce,d_00ea,d_abskwp,d_0031,d_0019,d_smac,d_0081,d_xen_l,d_xen_r";

        public static Regex VinRegex = new Regex(@"^(?!0{7,})([a-zA-Z0-9]{7,})$");

        private static readonly Tuple<string, string, string>[] ReadVinJobsBmwFast =
        {
            new Tuple<string, string, string>("G_ZGW", "STATUS_VIN_LESEN", "STAT_VIN"),
            new Tuple<string, string, string>("ZGW_01", "STATUS_VIN_LESEN", "STAT_VIN"),
            new Tuple<string, string, string>("G_CAS", "STATUS_FAHRGESTELLNUMMER", "STAT_FGNR17_WERT"),
            new Tuple<string, string, string>("D_CAS", "STATUS_FAHRGESTELLNUMMER", "FGNUMMER"),
        };

        private static readonly Tuple<string, string, string>[] ReadIdentJobsBmwFast =
        {
            new Tuple<string, string, string>("G_ZGW", "STATUS_VCM_GET_FA", "STAT_BAUREIHE"),
            new Tuple<string, string, string>("ZGW_01", "STATUS_VCM_GET_FA", "STAT_BAUREIHE"),
            new Tuple<string, string, string>("D_CAS", "C_FA_LESEN", "FAHRZEUGAUFTRAG"),
            new Tuple<string, string, string>("D_LM", "C_FA_LESEN", "FAHRZEUGAUFTRAG"),
            new Tuple<string, string, string>("D_KBM", "C_FA_LESEN", "FAHRZEUGAUFTRAG"),
        };

        private static readonly Tuple<string, string>[] ReadILevelJobsBmwFast =
        {
            new Tuple<string, string>("G_ZGW", "STATUS_I_STUFE_LESEN_MIT_SIGNATUR"),
            new Tuple<string, string>("G_ZGW", "STATUS_VCM_I_STUFE_LESEN"),
            new Tuple<string, string>("G_FRM", "STATUS_VCM_I_STUFE_LESEN"),
        };

        private static readonly Tuple<string, string, string>[] ReadVinJobsDs2 =
        {
            new Tuple<string, string, string>("ZCS_ALL", "FGNR_LESEN", "FG_NR"),
            new Tuple<string, string, string>("D_0080", "AIF_FG_NR_LESEN", "AIF_FG_NR"),
            new Tuple<string, string, string>("D_0010", "AIF_LESEN", "AIF_FG_NR"),
        };
        private static readonly Tuple<string, string, string>[] ReadIdentJobsDs2 =
        {
            new Tuple<string, string, string>("FZGIDENT", "GRUNDMERKMALE_LESEN", "BR_TXT"),
            new Tuple<string, string, string>("FZGIDENT", "STRINGS_LESEN", "BR_TXT"),
        };
        private static readonly string[] ReadMotorJobsDs2 =
        {
            "D_0012", "D_MOTOR", "D_0010", "D_0013", "D_0014"
        };

        public DetectVehicleBmw(EdiabasNet ediabas, string bmwDir)
        {
            _ediabas = ediabas;
            _bmwDir = bmwDir;
        }

        public bool DetectVehicleBmwFast()
        {
            _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "Try to detect vehicle BMW fast");
            ResetValues();
            HashSet<string> invalidSgbdSet = new HashSet<string>();

            try
            {
                List<Dictionary<string, EdiabasNet.ResultData>> resultSets;

                ProgressFunc?.Invoke(0);

                string detectedVin = null;
                int jobCount = ReadVinJobsBmwFast.Length + ReadIdentJobsBmwFast.Length + ReadILevelJobsBmwFast.Length;
                int index = 0;
                foreach (Tuple<string, string, string> job in ReadVinJobsBmwFast)
                {
                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Read VIN job: {0}", job.Item1);
                    try
                    {
                        if (AbortFunc != null && AbortFunc())
                        {
                            return false;
                        }

                        ProgressFunc?.Invoke(100 * index / jobCount);

                        ActivityCommon.ResolveSgbdFile(_ediabas, job.Item1);

                        _ediabas.ArgString = string.Empty;
                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob(job.Item2);

                        resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            if (detectedVin == null)
                            {
                                detectedVin = string.Empty;
                            }
                            Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                            if (resultDict.TryGetValue(job.Item3, out EdiabasNet.ResultData resultData))
                            {
                                string vin = resultData.OpData as string;
                                // ReSharper disable once AssignNullToNotNullAttribute
                                if (!string.IsNullOrEmpty(vin) && VinRegex.IsMatch(vin))
                                {
                                    detectedVin = vin;
                                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected VIN: {0}", detectedVin);
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        invalidSgbdSet.Add(job.Item1);
                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "No VIN response");
                        // ignored
                    }
                    index++;
                }

                if (string.IsNullOrEmpty(detectedVin))
                {
                    return false;
                }

                Vin = detectedVin;
                string vehicleType = null;
                string modelSeries = null;
                DateTime? cDate = null;

                foreach (Tuple<string, string, string> job in ReadIdentJobsBmwFast)
                {
                    if (AbortFunc != null && AbortFunc())
                    {
                        return false;
                    }

                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Read BR job: {0},{1}", job.Item1, job.Item2);
                    if (invalidSgbdSet.Contains(job.Item1))
                    {
                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Job ignored: {0}", job.Item1);
                        index++;
                        continue;
                    }
                    try
                    {
                        bool readFa = string.Compare(job.Item2, "C_FA_LESEN", StringComparison.OrdinalIgnoreCase) == 0;
                        ProgressFunc?.Invoke(100 * index / jobCount);

                        ActivityCommon.ResolveSgbdFile(_ediabas, job.Item1);

                        _ediabas.ArgString = string.Empty;
                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob(job.Item2);

                        resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                            if (resultDict.TryGetValue(job.Item3, out EdiabasNet.ResultData resultData))
                            {
                                if (readFa)
                                {
                                    string fa = resultData.OpData as string;
                                    if (!string.IsNullOrEmpty(fa))
                                    {
                                        ActivityCommon.ResolveSgbdFile(_ediabas, "FA");

                                        _ediabas.ArgString = "1;" + fa;
                                        _ediabas.ArgBinaryStd = null;
                                        _ediabas.ResultsRequests = string.Empty;
                                        _ediabas.ExecuteJob("FA_STREAM2STRUCT");

                                        List<Dictionary<string, EdiabasNet.ResultData>> resultSetsFa = _ediabas.ResultSets;
                                        if (resultSetsFa != null && resultSetsFa.Count >= 2)
                                        {
                                            Dictionary<string, EdiabasNet.ResultData> resultDictFa = resultSetsFa[1];
                                            if (resultDictFa.TryGetValue("BR", out EdiabasNet.ResultData resultDataBa))
                                            {
                                                string br = resultDataBa.OpData as string;
                                                if (!string.IsNullOrEmpty(br))
                                                {
                                                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected BR: {0}", br);
                                                    string vtype = VehicleInfoBmw.GetVehicleTypeFromBrName(br, _ediabas);
                                                    if (!string.IsNullOrEmpty(vtype))
                                                    {
                                                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected vehicle type: {0}", vtype);
                                                        vehicleType = vtype;
                                                    }
                                                }
                                            }

                                            if (resultDictFa.TryGetValue("C_DATE", out EdiabasNet.ResultData resultDataCDate))
                                            {
                                                string cDateStr = resultDataCDate.OpData as string;
                                                DateTime? dateTime = VehicleInfoBmw.ConvertConstructionDate(cDateStr);
                                                if (dateTime != null)
                                                {
                                                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected construction date: {0}",
                                                        dateTime.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                                                    cDate = dateTime.Value;
                                                }
                                            }

                                            if (vehicleType != null)
                                            {
                                                break;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    string br = resultData.OpData as string;
                                    if (!string.IsNullOrEmpty(br))
                                    {
                                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected BR: {0}", br);
                                        string vtype = VehicleInfoBmw.GetVehicleTypeFromBrName(br, _ediabas);
                                        if (!string.IsNullOrEmpty(vtype))
                                        {
                                            _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected vehicle type: {0}", vtype);
                                            modelSeries = br;
                                            vehicleType = vtype;
                                        }

                                        if (resultDict.TryGetValue("STAT_ZEIT_KRITERIUM", out EdiabasNet.ResultData resultDataCDate))
                                        {
                                            string cDateStr = resultDataCDate.OpData as string;
                                            DateTime? dateTime = VehicleInfoBmw.ConvertConstructionDate(cDateStr);
                                            if (dateTime != null)
                                            {
                                                _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected construction date: {0}",
                                                    dateTime.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                                                cDate = dateTime;
                                            }
                                        }

                                        if (vehicleType != null)
                                        {
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "No BR response");
                        // ignored
                    }
                    index++;
                }

                ProgressFunc?.Invoke(100);

                if (string.IsNullOrEmpty(vehicleType))
                {
                    vehicleType = VehicleInfoBmw.GetVehicleTypeFromVin(detectedVin, _ediabas, _bmwDir);
                }

                ModelSeries = modelSeries;
                Series = vehicleType;
                if (cDate.HasValue)
                {
                    ConstructYear = cDate.Value.ToString("yyyy", CultureInfo.InvariantCulture);
                    ConstructMonth = cDate.Value.ToString("MM", CultureInfo.InvariantCulture);
                }

                VehicleStructsBmw.VehicleSeriesInfo vehicleSeriesInfo = VehicleInfoBmw.GetVehicleSeriesInfo(vehicleType, cDate, _ediabas);
                if (vehicleSeriesInfo == null)
                {
                    _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "Vehicle series info not found");
                    return false;
                }
                _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Group SGBD: {0}", vehicleSeriesInfo.BrSgbd);
                GroupSgdb = vehicleSeriesInfo.BrSgbd;

                string iLevelShip = null;
                string iLevelCurrent = null;
                string iLevelBackup = null;
                foreach (Tuple<string, string> job in ReadILevelJobsBmwFast)
                {
                    if (AbortFunc != null && AbortFunc())
                    {
                        return false;
                    }

                    ProgressFunc?.Invoke(100 * index / jobCount);

                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Read ILevel job: {0},{1}", job.Item1, job.Item2);
                    if (invalidSgbdSet.Contains(job.Item1))
                    {
                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Job ignored: {0}", job.Item1);
                        index++;
                        continue;
                    }

                    try
                    {
                        _ediabas.ResolveSgbdFile(job.Item1);

                        _ediabas.ArgString = string.Empty;
                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob(job.Item2);

                        resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                            if (resultDict.TryGetValue("STAT_I_STUFE_WERK", out EdiabasNet.ResultData resultData))
                            {
                                string iLevel = resultData.OpData as string;
                                if (!string.IsNullOrEmpty(iLevel) && iLevel.Length >= 4 &&
                                    string.Compare(iLevel, VehicleInfoBmw.ResultUnknown,
                                        StringComparison.OrdinalIgnoreCase) != 0)
                                {
                                    iLevelShip = iLevel;
                                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected ILevel ship: {0}", iLevelShip);
                                }
                            }

                            if (!string.IsNullOrEmpty(iLevelShip))
                            {
                                if (resultDict.TryGetValue("STAT_I_STUFE_HO", out resultData))
                                {
                                    string iLevel = resultData.OpData as string;
                                    if (!string.IsNullOrEmpty(iLevel) && iLevel.Length >= 4 &&
                                        string.Compare(iLevel, VehicleInfoBmw.ResultUnknown,
                                            StringComparison.OrdinalIgnoreCase) != 0)
                                    {
                                        iLevelCurrent = iLevel;
                                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected ILevel current: {0}", iLevelCurrent);
                                    }
                                }

                                if (string.IsNullOrEmpty(iLevelCurrent))
                                {
                                    iLevelCurrent = iLevelShip;
                                }

                                if (resultDict.TryGetValue("STAT_I_STUFE_HO_BACKUP", out resultData))
                                {
                                    string iLevel = resultData.OpData as string;
                                    if (!string.IsNullOrEmpty(iLevel) && iLevel.Length >= 4 &&
                                        string.Compare(iLevel, VehicleInfoBmw.ResultUnknown,
                                            StringComparison.OrdinalIgnoreCase) != 0)
                                    {
                                        iLevelBackup = iLevel;
                                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected ILevel backup: {0}",
                                            iLevelBackup);
                                    }
                                }

                                break;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "No ILevel response");
                        // ignored
                    }

                    index++;
                }

                if (string.IsNullOrEmpty(iLevelShip))
                {
                    _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "ILevel not found");
                }
                else
                {
                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "ILevel: Ship={0}, Current={1}, Backup={2}",
                        iLevelShip, iLevelCurrent, iLevelBackup);

                    ILevelShip = iLevelShip;
                    ILevelCurrent = iLevelCurrent;
                    ILevelBackup = iLevelBackup;
                }

                Ds2Vehicle = false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool DetectVehicleDs2()
        {
            _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "Try to detect DS2 vehicle");
            ResetValues();

            try
            {
                List<Dictionary<string, EdiabasNet.ResultData>> resultSets;

                ProgressFunc?.Invoke(0);

                string groupFiles = null;
                try
                {
                    ActivityCommon.ResolveSgbdFile(_ediabas, "d_0044");

                    _ediabas.ArgString = "6";
                    _ediabas.ArgBinaryStd = null;
                    _ediabas.ResultsRequests = string.Empty;
                    _ediabas.ExecuteJob("KD_DATEN_LESEN");

                    string kdData1 = null;
                    resultSets = _ediabas.ResultSets;
                    if (resultSets != null && resultSets.Count >= 2)
                    {
                        Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                        if (resultDict.TryGetValue("KD_DATEN_TEXT", out EdiabasNet.ResultData resultData))
                        {
                            if (resultData.OpData is string)
                            {
                                kdData1 = (string)resultData.OpData;
                            }
                        }
                    }

                    _ediabas.ArgString = "7";
                    _ediabas.ArgBinaryStd = null;
                    _ediabas.ResultsRequests = string.Empty;
                    _ediabas.ExecuteJob("KD_DATEN_LESEN");

                    string kdData2 = null;
                    resultSets = _ediabas.ResultSets;
                    if (resultSets != null && resultSets.Count >= 2)
                    {
                        Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                        if (resultDict.TryGetValue("KD_DATEN_TEXT", out EdiabasNet.ResultData resultData))
                        {
                            if (resultData.OpData is string)
                            {
                                kdData2 = (string)resultData.OpData;
                            }
                        }
                    }

                    if (AbortFunc != null && AbortFunc())
                    {
                        return false;
                    }

                    if (!string.IsNullOrEmpty(kdData1) && !string.IsNullOrEmpty(kdData2))
                    {
                        ActivityCommon.ResolveSgbdFile(_ediabas, "grpliste");

                        _ediabas.ArgString = kdData1 + kdData2 + ";ja";
                        _ediabas.ArgBinaryStd = null;
                        _ediabas.ResultsRequests = string.Empty;
                        _ediabas.ExecuteJob("GRUPPENDATEI_ERZEUGE_LISTE_AUS_DATEN");

                        resultSets = _ediabas.ResultSets;
                        if (resultSets != null && resultSets.Count >= 2)
                        {
                            Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                            if (resultDict.TryGetValue("GRUPPENDATEI", out EdiabasNet.ResultData resultData))
                            {
                                if (resultData.OpData is string)
                                {
                                    groupFiles = (string)resultData.OpData;
                                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "KD group files: {0}", groupFiles);
                                }
                            }
                        }
                    }

                    if (ActivityCommon.ScanAllEcus)
                    {
                        _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "Scall all ECUs requested, ignoring detected groups");
                        groupFiles = null;
                    }

                    if (string.IsNullOrEmpty(groupFiles))
                    {
                        _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "KD data empty, using fallback");
                        groupFiles = AllDs2GroupFiles;
                    }
                }
                catch (Exception)
                {
                    _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "Read KD data failed");
                    // ignored
                }

                string detectedVin = null;

                if (!string.IsNullOrEmpty(groupFiles))
                {
                    int index = 0;
                    foreach (Tuple<string, string, string> job in ReadVinJobsDs2)
                    {
                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Read VIN job: {0}", job.Item1);
                        try
                        {
                            ProgressFunc?.Invoke(100 * index / ReadVinJobsDs2.Length);
                            ActivityCommon.ResolveSgbdFile(_ediabas, job.Item1);

                            _ediabas.ArgString = string.Empty;
                            _ediabas.ArgBinaryStd = null;
                            _ediabas.ResultsRequests = string.Empty;
                            _ediabas.ExecuteJob(job.Item2);

                            resultSets = _ediabas.ResultSets;
                            if (resultSets != null && resultSets.Count >= 2)
                            {
                                Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                                if (resultDict.TryGetValue(job.Item3, out EdiabasNet.ResultData resultData))
                                {
                                    string vin = resultData.OpData as string;
                                    // ReSharper disable once AssignNullToNotNullAttribute
                                    if (!string.IsNullOrEmpty(vin) && VinRegex.IsMatch(vin))
                                    {
                                        detectedVin = vin;
                                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected VIN: {0}", detectedVin);
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "No VIN response");
                            // ignored
                        }
                        index++;
                    }
                }
                else
                {
                    int index = 0;
                    foreach (string fileName in ReadMotorJobsDs2)
                    {
                        try
                        {
                            _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Read motor job: {0}", fileName);

                            ProgressFunc?.Invoke(100 * index / ReadMotorJobsDs2.Length);
                            ActivityCommon.ResolveSgbdFile(_ediabas, fileName);

                            _ediabas.ArgString = string.Empty;
                            _ediabas.ArgBinaryStd = null;
                            _ediabas.ResultsRequests = string.Empty;
                            _ediabas.ExecuteJob("IDENT");

                            resultSets = _ediabas.ResultSets;
                            if (resultSets != null && resultSets.Count >= 2)
                            {
                                Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                                if (EdiabasThread.IsJobStatusOk(resultDict))
                                {
                                    groupFiles = fileName;
                                    Pin78ConnectRequire = true;
                                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Motor ECUs detected: {0}", groupFiles);
                                    break;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                        index++;
                    }
                }

                Vin = detectedVin;
                int modelYear = VehicleInfoBmw.GetModelYearFromVin(detectedVin);
                if (modelYear >= 0)
                {
                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Model year: {0}", modelYear);
                    ConstructYear = string.Format(CultureInfo.InvariantCulture, "{0:0000}", modelYear);
                    ConstructMonth = string.Empty;
                }

                string vehicleType = null;
                if (!string.IsNullOrEmpty(detectedVin) && detectedVin.Length == 17)
                {
                    string typeSnr = detectedVin.Substring(3, 4);
                    _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Type SNR: {0}", typeSnr);
                    foreach (Tuple<string, string, string> job in ReadIdentJobsDs2)
                    {
                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Read vehicle type job: {0},{1}", job.Item1, job.Item2);
                        try
                        {
                            ActivityCommon.ResolveSgbdFile(_ediabas, job.Item1);

                            _ediabas.ArgString = typeSnr;
                            _ediabas.ArgBinaryStd = null;
                            _ediabas.ResultsRequests = string.Empty;
                            _ediabas.ExecuteJob(job.Item2);

                            resultSets = _ediabas.ResultSets;
                            if (resultSets != null && resultSets.Count >= 2)
                            {
                                Dictionary<string, EdiabasNet.ResultData> resultDict = resultSets[1];
                                if (resultDict.TryGetValue(job.Item3, out EdiabasNet.ResultData resultData))
                                {
                                    string detectedType = resultData.OpData as string;
                                    if (!string.IsNullOrEmpty(vehicleType) &&
                                        string.Compare(vehicleType, VehicleInfoBmw.ResultUnknown, StringComparison.OrdinalIgnoreCase) != 0)
                                    {
                                        vehicleType = detectedType;
                                        _ediabas.LogFormat(EdiabasNet.EdLogLevel.Ifh, "Detected Vehicle type: {0}", vehicleType);
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            _ediabas.LogString(EdiabasNet.EdLogLevel.Ifh, "No vehicle type response");
                            // ignored
                        }
                    }
                }

                if (string.IsNullOrEmpty(vehicleType))
                {
                    vehicleType = VehicleInfoBmw.GetVehicleTypeFromVin(detectedVin, _ediabas, _bmwDir);
                }

                Series = vehicleType;
                Ds2GroupFiles = groupFiles;

                if (string.IsNullOrEmpty(groupFiles))
                {
                    return false;
                }

                Ds2Vehicle = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool IsDs2GroupSgbd(string name)
        {
            string nameTrim = name.Trim();
            string[] groupArray = AllDs2GroupFiles.Split(',');
            foreach (string group in groupArray)
            {
                if (string.Compare(group, nameTrim, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResetValues()
        {
            Ds2Vehicle = false;
            Vin = null;
            GroupSgdb = null;
            ModelSeries = null;
            Series = null;
            Ds2GroupFiles = null;
            ConstructYear = null;
            ConstructMonth = null;
            Pin78ConnectRequire = false;
        }
    }
}
