﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using log4net;
using WebPsdzClient.App_Data;

namespace WebPsdzClient
{
    public partial class _Default : Page
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(_Default));

        protected void Page_Init(object sender, EventArgs e)
        {
            log.InfoFormat("_Default Page_Init");
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            log.InfoFormat("_Default Page_Load");
            SessionContainer sessionContainer = GetSessionContainer();
            if (sessionContainer == null)
            {
                return;
            }

            sessionContainer.UpdateDisplayEvent += UpdateDisplay;
            UpdateDisplay();
        }

        protected void Page_Unload(object sender, EventArgs e)
        {
            log.InfoFormat("_Default Page_Unload");
            SessionContainer sessionContainer = GetSessionContainer();
            if (sessionContainer == null)
            {
                return;
            }

            sessionContainer.UpdateDisplayEvent -= UpdateDisplay;
        }

        protected void ButtonStartHost_Click(object sender, EventArgs e)
        {
            SessionContainer sessionContainer = GetSessionContainer();
            if (sessionContainer == null)
            {
                return;
            }

            sessionContainer.Cts = new CancellationTokenSource();
            sessionContainer.StartProgrammingServiceTask(Global.IstaFolder).ContinueWith(task =>
            {
                sessionContainer.TaskActive = false;
                sessionContainer.Cts.Dispose();
                sessionContainer.Cts = null;
                UpdateDisplay();
            });

            sessionContainer.TaskActive = true;
            UpdateDisplay();
        }

        protected void ButtonStopHost_Click(object sender, EventArgs e)
        {
            SessionContainer sessionContainer = GetSessionContainer();
            if (sessionContainer == null)
            {
                return;
            }

            sessionContainer.StopProgrammingServiceTask().ContinueWith(task =>
            {
                sessionContainer.TaskActive = false;
                UpdateDisplay();
            });

            sessionContainer.TaskActive = false;
            UpdateDisplay();
        }

        protected void TimerUpdate_Tick(object sender, EventArgs e)
        {
            UpdateStatus(false);
        }

        private SessionContainer GetSessionContainer()
        {
            if (Session.Contents[Global.SessionContainerName] is SessionContainer sessionContainer)
            {
                return sessionContainer;
            }

            log.ErrorFormat("GetSessionContainer No SessionContainer");
            return null;
        }

        private void UpdateDisplay()
        {
            UpdateStatus();
        }

        private void UpdateStatus(bool updatePanel = true)
        {
            try
            {
                SessionContainer sessionContainer = GetSessionContainer();
                if (sessionContainer == null)
                {
                    return;
                }

                bool active = sessionContainer.TaskActive;
                bool abortPossible = sessionContainer.Cts != null;
                bool hostRunning = false;
                bool vehicleConnected = false;
                bool talPresent = false;
                if (!active)
                {
                    hostRunning = sessionContainer.ProgrammingJobs.ProgrammingService != null && sessionContainer.ProgrammingJobs.ProgrammingService.IsPsdzPsdzServiceHostInitialized();
                }

                ButtonStartHost.Enabled = !active && !hostRunning;
                ButtonStopHost.Enabled = !active && hostRunning;

                TextBoxStatus.Text = sessionContainer.StatusText;
                if (updatePanel && !UpdatePanelStatus.IsInPartialRendering)
                {
                    UpdatePanelStatus.Update();
                }
            }
            catch (Exception e)
            {
                log.ErrorFormat("UpdateStatus Exception: {0}", e.Message);
            }
        }
    }
}