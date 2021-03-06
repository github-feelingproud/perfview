﻿using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.ComponentModel;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics.Tracing;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using Triggers;
using System.Text;

// TODO use or delete
namespace PerfView
{
    /// <summary>
    /// Interaction logic for RunCommandDialog.xaml
    /// </summary>
    public partial class RunCommandDialog : Window
    {
        public RunCommandDialog(CommandLineArgs args, MainWindow mainWindow, bool isCollect = false, Action continuation = null)
        {
            Owner = mainWindow;
            if (mainWindow.CollectWindow != null)
                throw new ApplicationException("Collection Dialog already open.");

            m_continuation = continuation;
            Closing += delegate(object sender, CancelEventArgs e)
            {
                mainWindow.CollectWindow = null;
            };

            App.CommandProcessor.LaunchPerfViewElevatedIfNeeded(isCollect ? "GuiCollect" : "GuiRun", args);

            InitializeComponent();

            var osVersion = Environment.OSVersion.Version.Major + Environment.OSVersion.Version.Minor / 10.0;
            if (osVersion < 6.2)        // CPU Counters only supported on Windows 8 and above
            {
                CpuCountersListButton.IsEnabled = false;
                CpuCountersTextBox.IsEnabled = false;
            }

            if (args.DataFile == null)
                args.DataFile = "PerfViewData.etl";
            else if (!args.DataFile.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
            {
                if (args.DataFile.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
                    args.DataFile = args.DataFile.Substring(0, args.DataFile.Length - 4);       // Strip off the .zip.
                else
                    args.DataFile = "PerfViewData.etl";
            }
            mainWindow.StatusBar.Log("Collection Dialog open.");

            m_args = args;
            m_isCollect = isCollect;
            m_mainWindow = mainWindow;

            CurrentDirTextBox.Text = Environment.CurrentDirectory;
            if (args.CommandLine != null)
            {
                CommandToRunTextBox.Text = args.CommandLine;
                CommandToRunTextBox.SelectAll();
            }
            DataFileNameTextBox.Text = args.DataFile;
            RundownTimeoutTextBox.Text = args.RundownTimeout.ToString();
            SampleIntervalTextBox.Text = args.CpuSampleMSec.ToString();
            MaxCollectTextBox.Text = args.MaxCollectSec == 0 ? "" : args.MaxCollectSec.ToString();
            StopTriggerTextBox.Text = args.StopOnPerfCounter == null ? "" : string.Join(",", args.StopOnPerfCounter);
            CircularTextBox.Text = args.CircularMB.ToString();

            ZipCheckBox.IsChecked = args.Zip;
            MergeCheckBox.IsChecked = args.Merge;

            // We are not running from the command line 
            if (CommandProcessor.IsGuiCollection(args))
            {
                // Then get the values from previous runs if present.  
                if (!ZipCheckBox.IsChecked.HasValue)
                {
                    string configZip;
                    if (App.ConfigData.TryGetValue("Zip", out configZip))
                        ZipCheckBox.IsChecked = string.Compare(configZip, "true", true) == 0;
                }
                if (!MergeCheckBox.IsChecked.HasValue)
                {
                    string configMerge;
                    if (App.ConfigData.TryGetValue("Merge", out configMerge))
                        MergeCheckBox.IsChecked = string.Compare(configMerge, "true", true) == 0;
                }
            }

            NoNGenRundownCheckBox.IsChecked = args.NoNGenRundown;

            if (args.CpuCounters != null)
                CpuCountersTextBox.Text = string.Join(" ", args.CpuCounters);

            // TODO give better feedback about what happens when conflicts happen.  
            if (args.ClrEvents != ClrTraceEventParser.Keywords.None)
                ClrCheckBox.IsChecked = true;

            var kernelBase = (KernelTraceEventParser.Keywords)(KernelTraceEventParser.Keywords.Default - KernelTraceEventParser.Keywords.Profile);
            if ((args.KernelEvents & kernelBase) == kernelBase)
                KernelBaseCheckBox.IsChecked = true;
            if ((args.KernelEvents & KernelTraceEventParser.Keywords.Profile) != 0)
                CpuSamplesCheckBox.IsChecked = true;

            if (args.GCOnly)
                GCOnlyCheckBox.IsChecked = true;
            if (args.GCCollectOnly)
                GCCollectOnlyCheckBox.IsChecked = true;
            if (args.DotNetAlloc)
                DotNetAllocCheckBox.IsChecked = true;
            if (args.DotNetAllocSampled)
                DotNetAllocSampledCheckBox.IsChecked = true;
            if (args.DotNetCalls)
                DotNetCallsCheckBox.IsChecked = true;
            if (args.JITInlining)
                JITInliningCheckBox.IsChecked = true;
            if ((args.ClrEvents & ClrTraceEventParser.Keywords.GCSampledObjectAllocationHigh) != 0)
                ETWDotNetAllocSampledCheckBox.IsChecked = true;
            if (args.NetworkCapture)
                NetCaptureCheckBox.IsChecked = true;
            if (args.NetMonCapture)
                NetMonCheckBox.IsChecked = true;

            if (args.DumpHeap)
                HeapSnapshotCheckBox.IsChecked = true;

            if (args.OSHeapExe != null)
                OSHeapExeTextBox.Text = args.OSHeapExe;
            if (args.OSHeapProcess != 0)
                OSHeapProcessTextBox.Text = args.OSHeapProcess.ToString();

            if ((args.KernelEvents & (KernelTraceEventParser.Keywords.ContextSwitch | KernelTraceEventParser.Keywords.Dispatcher)) != 0)
                ThreadTimeCheckbox.IsChecked = true;
            if ((args.KernelEvents & KernelTraceEventParser.Keywords.Memory) != 0)
                MemoryCheckBox.IsChecked = true;
            if ((args.KernelEvents & KernelTraceEventParser.Keywords.Registry) != 0)
                RegistryCheckBox.IsChecked = true;
            if ((args.KernelEvents & KernelTraceEventParser.Keywords.FileIOInit) != 0)
                FileIOCheckBox.IsChecked = true;
            if ((args.KernelEvents & KernelTraceEventParser.Keywords.VirtualAlloc) != 0)
                VirtualAllocCheckBox.IsChecked = true;
            if ((args.KernelEvents & KernelTraceEventParser.Keywords.ReferenceSet) != 0)
               RefSetCheckBox.IsChecked = true;
            if ((args.KernelEvents & KernelTraceEventParser.Keywords.Handle) != 0)
                HandleCheckBox.IsChecked = true;

            if (args.Providers != null)
            {
                var str = "";
                foreach (var provider in args.Providers)
                {
                    if (string.Compare(provider, "Microsoft-Windows-IIS", StringComparison.OrdinalIgnoreCase) == 0)
                        IISCheckBox.IsChecked = true;
                    if (string.Compare(provider, "ClrStress", StringComparison.OrdinalIgnoreCase) == 0)
                        StressCheckBox.IsChecked = true;
                    else if (string.Compare(provider, "Microsoft-Windows-Kernel-Memory") == 0)
                        MemInfoCheckBox.IsChecked = true;
                    else
                    {
                        if (str.Length != 0)
                            str += ",";
                        str += provider;
                    }
                }
                AdditionalProvidersTextBox.Text = str;
            }

            if (args.Message != null)
                MarkTextBox.Text = args.Message;
            else
                MarkTextBox.Text = "Mark 1";

            // TODO the defaults are wrong if you switch from run to collect and back 
            if (isCollect)
            {
                Title = "Collecting data over a user specified interval";
                CommandToRunTextBox.Text = "** Machine Wide **";
                CommandToRunTextBox.IsEnabled = false;
                RundownCheckBox.IsChecked = !args.NoRundown;
                RundownTimeoutTextBox.IsEnabled = !args.NoRundown;
                if (args.CircularMB == 0)
                    CircularTextBox.Text = "500";

                OKButton.Content = "Start Collection";
                StatusTextBox.Text = "Press Start Collection to Start.";
                DataFileNameTextBox.Focus();
            }
            else
            {
                CommandToRunTextBox.Focus();
            }
        }

        #region private
        internal void StartCollection()
        {
            m_collectionRunning = true;       // TODO this is hack
            StatusTextBox.Text = "Collecting data... Click Button to stop.";
            m_timer = new DispatcherTimer();
            m_timer.Interval = new TimeSpan(0, 0, 0, 1, 0);
            m_timer.Tick += OnTick;
            m_timer.IsEnabled = true;
            m_startTime = DateTime.Now;
            m_startedDropping = "";
            OKButton.Content = "Stop Collection";
        }

        private void CommandToRunKeyDown(object sender, KeyEventArgs e)
        {
            if (!m_isCollect && e.Key == Key.Return)
            {
                OKButtonClick(sender, null);
                e.Handled = true;
            }
        }
        private void DoHyperlinkHelp(object sender, ExecutedRoutedEventArgs e)
        {
            MainWindow.DisplayUsersGuide(e.Parameter as string);
        }
        private void DataFileKeyDown(object sender, KeyEventArgs e)
        {
            if (m_isCollect && e.Key == Key.Return)
            {
                OKButtonClick(sender, null);
                e.Handled = true;
            }
        }
        private void DataFileButtonClick(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog();
            saveDialog.FileName = DataFileNameTextBox.Text;
            saveDialog.InitialDirectory = Environment.CurrentDirectory;
            saveDialog.Title = "Output Data File Name";
            saveDialog.DefaultExt = ".etl";                          // Default file extension
            saveDialog.Filter = "ETW Log Files|*.etl|All Files|*.*";
            saveDialog.AddExtension = true;
            saveDialog.OverwritePrompt = true;

            // Show open file dialog box
            Nullable<bool> result = saveDialog.ShowDialog();
            if (result == true)
                DataFileNameTextBox.Text = saveDialog.FileName;
        }
        private void ProviderBrowserButtonClick(object sender, RoutedEventArgs e)
        {
            //AdditionalProvidersTextBox
           // public delegate void UpdateAdditionalProviders(string additionalProvider){AdditionalProvidersTextBox.Text = additionalProvider}
            PerfView.Dialogs.ProviderBrowser providerBrowserWindow = new Dialogs.ProviderBrowser(this, delegate(string additionalProvider, string keys, string level) { 
                AdditionalProvidersTextBox.Text = MergeProvider(AdditionalProvidersTextBox.Text, additionalProvider,keys,level); 
            });
            providerBrowserWindow.ShowDialog();
        }
        private void RundownCheckboxClick(object sender, RoutedEventArgs e)
        {
            RundownTimeoutTextBox.IsEnabled = RundownCheckBox.IsChecked ?? false;
        }
        private void ZipCheckBoxClicked(object sender, RoutedEventArgs e)
        {
            if (ZipCheckBox.IsChecked ?? false)
                MergeCheckBox.IsChecked = true;
            m_mergeOrZipCheckboxTouched = true;
        }
        private void MergeCheckBoxClicked(object sender, RoutedEventArgs e)
        {
            // Not merging means not Zipping.  
            if (!(MergeCheckBox.IsChecked ?? true))
                ZipCheckBox.IsChecked = false;
            m_mergeOrZipCheckboxTouched = true;
        }

        // This routine is called when you click the OK button to START collection 
        private void OKButtonClick(object sender, RoutedEventArgs e)
        {
            // Handled by the action itself.  TODO is this a hack?
            if (m_collectionRunning)
                return;

            bool shouldClose = true;
            try
            {
                if (CommandToRunTextBox.Text.Length == 0)
                {
                    m_mainWindow.StatusBar.LogError("No command given.");
                    return;
                }

                if (!m_isCollect)
                    m_args.CommandLine = CommandToRunTextBox.Text;
                m_args.DataFile = DataFileNameTextBox.Text;

                if (!int.TryParse(RundownTimeoutTextBox.Text, out m_args.RundownTimeout))
                {
                    m_mainWindow.StatusBar.LogError("Could not parse rundown timeout value: " + RundownTimeoutTextBox.Text);
                    return;
                }

                if (!float.TryParse(SampleIntervalTextBox.Text, out m_args.CpuSampleMSec))
                {
                    m_mainWindow.StatusBar.LogError("Could not parse sample interval timeout value: " + SampleIntervalTextBox.Text);
                    return;
                }

                if (MaxCollectTextBox.Text.Length == 0)
                    m_args.MaxCollectSec = 0;
                else if (!int.TryParse(MaxCollectTextBox.Text, out m_args.MaxCollectSec))
                {
                    m_mainWindow.StatusBar.LogError("Could not parse max collection value: " + MaxCollectTextBox.Text);
                    return;
                }

                if (StopTriggerTextBox.Text.Length == 0)
                    m_args.StopOnPerfCounter = null;
                else
                {
                    try
                    {
                        (new PerformanceCounterTrigger(StopTriggerTextBox.Text, 0, m_mainWindow.StatusBar.LogWriter, null)).Dispose();
                    }
                    catch (Exception ex)
                    {
                        m_mainWindow.StatusBar.LogError("Error in StopTrigger: {0}" + ex.Message);
                        return;
                    }
                    m_args.StopOnPerfCounter = StopTriggerTextBox.Text.Split(',');
                }

                if (m_args.CpuSampleMSec < .125F)
                {
                    m_args.CpuSampleMSec = .125F;
                    SampleIntervalTextBox.Text = "0.125";
                    m_mainWindow.StatusBar.LogError("Sample interval below the .125 miniumum, setting to .125MSec.");
                }

                if (!int.TryParse(CircularTextBox.Text, out m_args.CircularMB))
                {
                    m_mainWindow.StatusBar.LogError("Could not parse circular value: " + CircularTextBox.Text);
                    return;
                }

                try
                {
                    Environment.CurrentDirectory = CurrentDirTextBox.Text;
                }
                catch (Exception)
                {
                    m_mainWindow.StatusBar.LogError("Could not set current directory to " + CurrentDirTextBox.Text);
                    return;
                }

                if (KernelBaseCheckBox.IsChecked ?? false)
                    m_args.KernelEvents = (KernelTraceEventParser.Keywords)(KernelTraceEventParser.Keywords.Default - KernelTraceEventParser.Keywords.Profile);
                if (CpuSamplesCheckBox.IsChecked ?? false)
                    m_args.KernelEvents |= KernelTraceEventParser.Keywords.Profile;

                if (ThreadTimeCheckbox.IsChecked ?? false)
                    m_args.KernelEvents |= KernelTraceEventParser.Keywords.ThreadTime;
                if (MemoryCheckBox.IsChecked ?? false)
                    m_args.KernelEvents |= KernelTraceEventParser.Keywords.MemoryHardFaults | KernelTraceEventParser.Keywords.Memory;
                if (FileIOCheckBox.IsChecked ?? false)
                    m_args.KernelEvents |= KernelTraceEventParser.Keywords.FileIOInit;
                if (RegistryCheckBox.IsChecked ?? false)
                    m_args.KernelEvents |= KernelTraceEventParser.Keywords.Registry;
                if (VirtualAllocCheckBox.IsChecked ?? false)
                    m_args.KernelEvents |= KernelTraceEventParser.Keywords.VirtualAlloc | KernelTraceEventParser.Keywords.VAMap;
                if (RefSetCheckBox.IsChecked ?? false)
                    m_args.KernelEvents |= KernelTraceEventParser.Keywords.ReferenceSet;
                if (HandleCheckBox.IsChecked ?? false)
                    m_args.KernelEvents |= KernelTraceEventParser.Keywords.Handle;

                if (!(ClrCheckBox.IsChecked ?? true))
                    m_args.ClrEvents = ClrTraceEventParser.Keywords.None;
                else if (m_args.ClrEvents == ClrTraceEventParser.Keywords.None)
                    m_args.ClrEvents = ClrTraceEventParser.Keywords.Default;

                m_args.NoNGenRundown = NoNGenRundownCheckBox.IsChecked ?? false;
                m_args.DotNetAlloc = DotNetAllocCheckBox.IsChecked ?? false;
                m_args.DotNetCalls = DotNetCallsCheckBox.IsChecked ?? false;
                m_args.DotNetAllocSampled = DotNetAllocSampledCheckBox.IsChecked ?? false;
                if (ETWDotNetAllocSampledCheckBox.IsChecked ?? false)
                    m_args.ClrEvents |= ClrTraceEventParser.Keywords.GCSampledObjectAllocationHigh;
                else
                    m_args.ClrEvents &= ~ClrTraceEventParser.Keywords.GCSampledObjectAllocationHigh;

                m_args.JITInlining = JITInliningCheckBox.IsChecked ?? false;
                if (m_args.JITInlining)
                {
                    m_args.ClrEvents |= ClrTraceEventParser.Keywords.JitTracing;
                }

                m_args.NetMonCapture = NetMonCheckBox.IsChecked ?? false;
                m_args.NetworkCapture = NetCaptureCheckBox.IsChecked ?? false;

                if (OSHeapExeTextBox.Text.Length > 0)
                    m_args.OSHeapExe = OSHeapExeTextBox.Text;
                else
                    m_args.OSHeapExe = null;

                if (OSHeapProcessTextBox.Text.Length > 0)
                {
                    if (!int.TryParse(OSHeapProcessTextBox.Text, out m_args.OSHeapProcess))
                    {
                        m_mainWindow.StatusBar.LogError("Could parse OS Heap Process ID '" + OSHeapProcessTextBox.Text + "' as a integer ");
                        return;
                    }
                }
                else
                    m_args.OSHeapProcess = 0;

                // TODO this logic is cloned.  We need it in only one place. 
                if (GCOnlyCheckBox.IsChecked ?? false)
                {
                    m_args.GCOnly = true;

                    // For stack parsing.  
                    m_args.KernelEvents = KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.Thread | KernelTraceEventParser.Keywords.ImageLoad;
                    m_args.ClrEvents = ClrTraceEventParser.Keywords.GC | ClrTraceEventParser.Keywords.GCHeapSurvivalAndMovement | ClrTraceEventParser.Keywords.Stack |
                                ClrTraceEventParser.Keywords.Jit | ClrTraceEventParser.Keywords.Loader | ClrTraceEventParser.Keywords.Exception;
                } 
                if (GCCollectOnlyCheckBox.IsChecked ?? false)
                {
                    m_args.GCCollectOnly = true;

                    // The process events are so we get process names.  The ImageLoad events are so that we get version information about the DLLs 
                    m_args.KernelEvents = KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ImageLoad;
                    m_args.ClrEvents = ClrTraceEventParser.Keywords.GC | ClrTraceEventParser.Keywords.Exception;
                    m_args.ClrEventLevel = TraceEventLevel.Informational;
                    m_args.NoRundown = true;
                    if (!m_args.Merge.HasValue)
                        m_args.Merge = false;
                }

                string cpuCounters = CpuCountersTextBox.Text;
                if (cpuCounters.Length != 0)
                    m_args.CpuCounters = cpuCounters.Split(' ');

                var providers = AdditionalProvidersTextBox.Text;
                if ((IISCheckBox.IsChecked ?? false) && providers.IndexOf("Microsoft-Windows-IIS", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (providers.Length != 0)
                        providers += ",";
                    providers += "Microsoft-Windows-IIS";
                }
                if ((StressCheckBox.IsChecked ?? false) && providers.IndexOf("ClrStress", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (providers.Length != 0)
                        providers += ",";
                    providers += "ClrStress";
                }
                if ((MemInfoCheckBox.IsChecked ?? false) && providers.IndexOf("Microsoft-Windows-Kernel-Memory", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (providers.Length != 0)
                        providers += ",";
                    providers += "Microsoft-Windows-Kernel-Memory";
                }

                if ((BackgroundJITCheckBox.IsChecked ?? false) && providers.IndexOf("ClrPrivate", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // currently we turn on CLRPrvate events at full verbosity.  If we change this please take a look in 
                    // JitProcess.Collect (search for StartupPrestubWorkerStart) and fix the logic by which we detect that
                    // background JIT events are on.  
                    if (providers.Length != 0)
                        providers += ",";
                    providers += "ClrPrivate";
                }

                m_args.DumpHeap = HeapSnapshotCheckBox.IsChecked ?? false;
                m_args.Finalizers = FinalizersCheckBox.IsChecked ?? false;

                if (providers.Length > 0)
                    m_args.Providers = providers.Split(',');
                else
                    m_args.Providers = null;

                // These three we don't copy back when you start collection and instead do it when we end collection
                // m_args.NoRundown = !(RundownCheckBox.IsChecked ?? false);
                // m_args.Merge = MergeCheckBox.IsChecked;
                // m_args.Zip = ZipCheckBox.IsChecked;
                m_args.Message = MarkTextBox.Text;

                // This also throws an exception if there are illegal chars.  
                var fullPath = System.IO.Path.GetFullPath(App.CommandLineArgs.DataFile);

                if (m_isCollect)
                {
                    StartCollection();
                    shouldClose = false;
                    m_mainWindow.ExecuteCommand("Collecting data " + fullPath, App.CommandProcessor.Collect, null, delegate()
                        {
                            m_timer.IsEnabled = false;
                            Hide();
                            if (m_continuation != null)
                                m_continuation();
                        },
                        delegate
                        {
                            Close();
                        }
                        );
                }
                else
                {
                    m_args.Zip = ZipCheckBox.IsChecked;
                    m_args.Merge = ZipCheckBox.IsChecked;
                    shouldClose = false;
                    Close();
                    m_mainWindow.ExecuteCommand("Running: " + App.CommandLineArgs.CommandLine + "...  See log for output.",
                        App.CommandProcessor.Run, null, m_continuation);
                }
            }
            finally
            {
                if (shouldClose)
                    Close();
            }
        }
        private void CancelButtonClick(object sender, RoutedEventArgs e)
        {
            if (m_mainWindow.StatusBar.IsWorking)
                m_mainWindow.StatusBar.AbortWork();
            else
                m_mainWindow.StatusBar.LogError("Data Collection canceled by user.");
            this.Close();
        }

        private void LogButtonClick(object sender, RoutedEventArgs e)
        {
            m_mainWindow.StatusBar.OpenLog();
        }

        private void OnTick(object sender, EventArgs e)
        {
            var status = CommandProcessor.GetStatusLine(m_args, m_startTime, ref m_startedDropping);
            StatusTextBox.Text = status + "  Click Stop button to stop.";
        }

        private void MarkTextKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                MarkButtonClick(sender, e);
            }
        }
        private void MarkButtonClick(object sender, RoutedEventArgs e)
        {
            var message = MarkTextBox.Text;
            StatusTextBox.Text = "Logged Event PerfView.Mark(" + message + ")";
            PerfViewLogger.Log.Mark(message);
            var m = Regex.Match(message, @"^(.*) (\d+)$");
            if (m.Success)
            {
                int num = int.Parse(m.Groups[2].Value);
                MarkTextBox.Text = m.Groups[1].Value + " " + (num + 1).ToString();
            }
        }

        private void DoExpand(object sender, RoutedEventArgs e)
        {
            // TODO I am trying to make the expander just grow the window, I do this by forcing the window height
            // to change.  However this causes double-redraw, and it also requires me to figure out the right constant
            // to add any time I change what is in the expander.   There is undoubtedly a better way...
            m_originalHeight = (int)Height;
            Height = m_originalHeight + 180;
        }
        private void DoCollapsed(object sender, RoutedEventArgs e)
        {
            Height = m_originalHeight;
        }

        private void DoCpuCountersListClick(object sender, RoutedEventArgs e)
        {
            var cpuCounterSpecs = new List<string>();
            var cpuCounters = TraceEventProfileSources.GetInfo();
            foreach (var cpuCounter in cpuCounters.Values)
            {
                var defaultCount = Math.Max(100000, cpuCounter.MinInterval);
                if (cpuCounter.Name == "Timer")
                    defaultCount = 10000;
                var cpuCounterSpec = cpuCounter.Name + ":" + defaultCount.ToString();
                cpuCounterSpecs.Add(cpuCounterSpec);
            }
            CpuCountersListBox.ItemsSource = cpuCounterSpecs;
            CpuCountersPopup.IsOpen = true;
        }
        private void DoCpuCountersListBoxKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                UpdateCpuCounters();
            }
            else if (e.Key == Key.Tab)
                UpdateCpuCounters();
            else if (e.Key == Key.Escape)
                CpuCountersPopup.IsOpen = false;
        }
        private void DoCpuCountersListBoxDoubleClick(object sender, MouseButtonEventArgs e)
        {
            UpdateCpuCounters();
        }
        private void UpdateCpuCounters()
        {
            CpuCountersPopup.IsOpen = false;
            string CpuCounters = "";
            string sep = "";
            foreach (var item in CpuCountersListBox.SelectedItems)
            {
                CpuCounters += sep + ((string)item);
                sep = " ";
            }
            CpuCountersTextBox.Text = CpuCounters;
        }

        private static string MergeProvider(string providerList, string additionalProvider, string providerKeys, string providerLevel)
        {
            string[] providersSpecs = providerList.Split(',');
            StringBuilder sb = new StringBuilder();
            if (providersSpecs.Length > 0)
            {
                if (providersSpecs[0] == "")
                    return sb.Append(additionalProvider).Append(":").Append(providerKeys).Append(':').Append(providerLevel).ToString();
                else
                {
                    if (!providersSpecs[0].StartsWith(additionalProvider))
                        sb.Append(providersSpecs[0]).Append(",");
                }
            }
            for (int i = 1; i < providersSpecs.Length; i++)
            {
                if (!providersSpecs[i].StartsWith(additionalProvider))
                    sb.Append(providersSpecs[i]).Append(",");
            }

            return sb.Append(additionalProvider).Append(":").Append(providerKeys).Append(':').Append(providerLevel).ToString();
        }



        int m_originalHeight;

        CommandLineArgs m_args;
        bool m_isCollect;
        internal bool m_collectionRunning;
        MainWindow m_mainWindow;
        DispatcherTimer m_timer;
        DateTime m_startTime;
        string m_startedDropping;
        Action m_continuation;
        internal bool m_mergeOrZipCheckboxTouched;
        #endregion
    }
}
