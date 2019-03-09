﻿using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

//
// DO NOT reference any file or library that is being updated because it becomes locked
// instead use AssemblyCaller.Call or AssemblyCaller.CallStatic to access external app variables
//
// DO NOT add any third party 'References' (dependencies) to AutoUpdater 
//
// AutoUpdater performs clean toolkit installation and merges some (TODO: all) user customized files for latest revsion
//
// Verbose messaging via the boolean isDebugMe can be turned on/off for debugging the executable
//

namespace RocksmithToolkitUpdater
{
    public partial class AutoUpdaterForm : Form
    {
        private enum DownloadStatus
        {
            WAIT, // initial download status
            ERROR,
            CANCEL,
            SUCCESS,
            UNKNOWN
        }

        private const string APP_CSZIPLIB = "ICSharpCode.SharpZipLib.dll";
        private const string APP_RSGUI = "RocksmithToolkitGUI.exe";
        private const string APP_RSLIB = "RocksmithToolkitLib.dll";
        private const string APP_UPDATER = "RocksmithToolkitUpdater.exe";
        private const string APP_UPDATING = "RocksmithToolkitUpdating.exe";
        private const string REPO_CONFIG = "RocksmithToolkitLib.Config.xml";
        private const string REPO_SONGAPPID = "RocksmithToolkitLib.SongAppId.xml";
        private const string REPO_TUNINGDEF = "RocksmithToolkitLib.TuningDefinition.xml";

        private DownloadStatus dlStatus;
        private WebClient webClient;
        private Stopwatch sw = new Stopwatch();
        private string latestZipPath;
        private string latestZipUrl;
        private string localToolkitDir;
        private string newLocalToolkitDir;
        private string localToolkitDirRoot;
        private string tempToolkitDir;
        private string lastError;
        private string appExecPath;
        private string appExecDir;
        private string appExecFile;
        private bool isInDesignMode;
        private bool isDebugMe;

        /// <summary>
        /// AutoUpdater with command line args for localToolkitDir and tempToolkitDir
        /// </summary>
        public AutoUpdaterForm(string[] args)
        {
            InitializeComponent();

            // force showing autoupdater and progress bar on startup
            this.Show();
            this.Location = new Point(100, 100);
            this.BringToFront();
            ShowCurrentOperation("Starting the AutoUpdater Engine ...");
            pbUpdate.Style = ProgressBarStyle.Marquee;
            pbUpdate.Refresh();
            this.Refresh();

            // catch if there are no cmd line arguments
            if (args.GetLength(0) == 0) args = new string[1] { "?" };
            if (args[0].Equals("?"))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Song Creator Toolkit for Rocksmith");
                sb.AppendLine("- commonly known as, 'the toolkit'");
                sb.AppendLine("");
                sb.AppendLine("You are probably seeing this informative message");
                sb.AppendLine("because you started the RocksmithToolkitUpdater.exe");
                sb.AppendLine("application by double clicking on it directly.");
                sb.AppendLine("");
                sb.AppendLine("- Normal Usage:");
                sb.AppendLine("  AutoUpdater is normally run programmatically by the");
                sb.AppendLine("  toolkit to automatically update the toolkit files.");
                sb.AppendLine("");
                sb.AppendLine("- Alternate Usage:");
                sb.AppendLine("  AutoUpdater can be run by double clicking on the");
                sb.AppendLine("  application.  This will force the toolkit to be");
                sb.AppendLine("  updated to the latest available online revision.");
                sb.AppendLine("");
                //sb.AppendLine("- WARNING:");
                //sb.AppendLine("  All user customized toolkit settings are overwritten");
                //sb.AppendLine("  if AutoUpdater is run in the Alternate Usage mode.");
                //sb.AppendLine("  A warning will popup during update to remind you.");
                //sb.AppendLine("");
                sb.AppendLine("Continue running AutoUpdater in Alternate Usage mode?   ");

                if (DialogResult.Yes != MessageBox.Show(sb.ToString(), "AutoUpdater", MessageBoxButtons.YesNo, MessageBoxIcon.Information))
                    Environment.Exit(0);

                // confirm toolkit process is not running before continuing
                Thread.Sleep(500);
                Process[] processesByName = Process.GetProcessesByName("RocksmithToolkitGUI");
                if (processesByName.Length != 0)
                {
                    MessageBox.Show("<ERROR> Detected that RocksmithToolkitGUI is running ..." + Environment.NewLine +
                        "The toolkit must be closed before running the AutoUpdater.  ", "RocksmithToolkit AutoUpdater", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    Environment.Exit(2);
                }
            }

            // turn on/off debugging MessageBox displays
            isDebugMe = false;
            isInDesignMode = Helpers.IsInDesignMode;
            appExecPath = Application.ExecutablePath;
            appExecDir = Path.GetDirectoryName(appExecPath);
            appExecFile = Path.GetFileName(appExecPath);
            // if (isInDesignMode) debugMe = true; // overrides initiation

            // running RocksmithToolkitUpdating.exe programatically (Primary Usage Mode)
            if (args.Length == 2 && appExecFile.Equals(APP_UPDATING, StringComparison.InvariantCultureIgnoreCase) || appExecFile.Equals(APP_RSGUI, StringComparison.InvariantCultureIgnoreCase))
            {
                if (isDebugMe)
                    MessageBox.Show("Starting Auto Update ... Primary Usage", "DPDM");

                localToolkitDir = args[0];
                tempToolkitDir = args[1];
            }
            // running RocksmithToolkitUpdater.exe (Alternate Usage Mode) or developer running project in VS IDE Debug mode
            else if (appExecFile.Equals(APP_UPDATER, StringComparison.InvariantCultureIgnoreCase))
            {
                // the user double clicked on RocksmithToolkitUpdater.exe (w/o cmd line args)
                localToolkitDir = appExecDir;
                tempToolkitDir = Path.Combine(Path.GetTempPath(), "RocksmithToolkit");

                if (Directory.Exists(tempToolkitDir))
                    Directory.Delete(tempToolkitDir, true);

                Directory.CreateDirectory(tempToolkitDir);

                // copy required files for debugging the AutoUpdater as a standalone project in VS IDE Debug mode
                if (isInDesignMode)
                {
                    if (isDebugMe)
                        MessageBox.Show("Starting Alternate Usage In Design Mode ...", "DPDM");

                    try
                    {
                        var rootBinDebugDir = Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.FullName, "RocksmithTookitGUI\\bin\\Debug");
                        File.Copy(Path.Combine(rootBinDebugDir, APP_CSZIPLIB), Path.Combine(appExecDir, APP_CSZIPLIB), true);
                        File.Copy(Path.Combine(rootBinDebugDir, APP_RSLIB), Path.Combine(appExecDir, APP_RSLIB), true);
                        File.Copy(Path.Combine(rootBinDebugDir, APP_RSGUI), Path.Combine(appExecDir, APP_RSGUI), true);
                        File.Copy(Path.Combine(rootBinDebugDir, REPO_CONFIG), Path.Combine(appExecDir, REPO_CONFIG), true);
                        File.Copy(Path.Combine(rootBinDebugDir, REPO_SONGAPPID), Path.Combine(appExecDir, REPO_SONGAPPID), true);
                        File.Copy(Path.Combine(rootBinDebugDir, REPO_TUNINGDEF), Path.Combine(appExecDir, REPO_TUNINGDEF), true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("<ERROR> Can not find required file(s) to run AutoUpdater project in VS IDE Debug mode.  " + Environment.NewLine + "Make sure the RocksmithToolkitGUI project has been run in VS IDE Debug mode first. " + Environment.NewLine + Environment.NewLine + ex.Message, "DPDM");

                        Environment.Exit(1);
                    }
                }
                else
                {
                    // use some whacky, hacky, trickery
                    // make a copy of AutoUpdater to prevent locking the process during update
                    var updaterAppPath = Path.Combine(localToolkitDir, APP_UPDATER);
                    var updatingAppPath = Path.Combine(tempToolkitDir, APP_UPDATING);
                    File.Copy(updaterAppPath, updatingAppPath, true);
                    var cmdArgs = String.Format("\"{0}\" \"{1}\"", localToolkitDir, tempToolkitDir);

                    if (isDebugMe)
                        MessageBox.Show("Starting Auto Update ... Alternate Usage Release Mode" + Environment.NewLine + "cmdArgs = " + cmdArgs, "DPDM");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = updatingAppPath,
                        Arguments = cmdArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true, // hide command window
                    };

                    using (var updater = new Process())
                    {
                        updater.StartInfo = startInfo;
                        updater.Start();
                    }

                    Thread.Sleep(500);
                    // Kill current process (RocksmithToolkitUpdater.exe) now
                    // that new process (RocksmithToolkitUpdating.exe) is started
                    Environment.Exit(0);
                }
            }
            else
            {
                MessageBox.Show("<ERROR> Unexpected updater usage ..." + Environment.NewLine + "appExecFile = " + appExecFile, "RocksmithToolkit AutoUpdater", MessageBoxButtons.OK, MessageBoxIcon.Error);

                Environment.Exit(1);
            }

            localToolkitDirRoot = Path.GetDirectoryName(localToolkitDir);
            // toolkit archive directory/file structure effects newLocalToolkitDir path
            newLocalToolkitDir = Path.Combine(localToolkitDirRoot, "RocksmithToolkit");

            if (isDebugMe)
                MessageBox.Show("IsDesignMode = " + isInDesignMode.ToString() + Environment.NewLine + "Currently running: " + Application.ExecutablePath + Environment.NewLine + "localToolkitDir = " + localToolkitDir + Environment.NewLine + "newlocalToolkitDir = " + newLocalToolkitDir + Environment.NewLine + "localToolkitDirRoot = " + localToolkitDirRoot + Environment.NewLine + "tempToolkitDir = " + tempToolkitDir + Environment.NewLine + "args[0] = " + args[0] + Environment.NewLine + "args[1] = " + args[1], "DPDM");

            // switch progress bar style
            pbUpdate.Style = ProgressBarStyle.Continuous;
            pbUpdate.Value = 0;

            try
            {
                // backup the local process
                BackupProcessDir(localToolkitDir, tempToolkitDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("<ERROR> Process backup failure ..." + Environment.NewLine + "Please manually download and install the latest toolkit revision." + Environment.NewLine + ex.Message, "RocksmithToolkit AutoUpdater", MessageBoxButtons.OK, MessageBoxIcon.Error);

                Environment.Exit(1);
            }

            try
            {
                // www.rscustom.net/builds/latest.zip or *.tar.gz
                // www.rscustom.net/builds/latest_test.zip or *.tar.gz
                // get latest zip url
                latestZipUrl = (string)AssemblyCaller.Call(Path.Combine(appExecDir, APP_RSLIB), "RocksmithToolkitLib.ToolkitVersionOnline", "GetFileUrl", null, true);

                if (String.IsNullOrEmpty(latestZipUrl))
                    throw new Exception("latestZipUrl is null/empty");
            }
            catch (Exception ex)
            {
                MessageBox.Show("<ERROR> latestZipUrl AssemblyCaller failure ..." + Environment.NewLine + ex.InnerException.Message, "RocksmithToolkit AutoUpdater", MessageBoxButtons.OK, MessageBoxIcon.Error);

                Environment.Exit(1);
            }

            var latestZipUri = new Uri(latestZipUrl);
            latestZipPath = Path.Combine(tempToolkitDir, Path.GetFileName(latestZipUri.LocalPath));
            DownloadFile(latestZipUri, latestZipPath);

            if (isDebugMe)
            {
                MessageBox.Show("Check backup files: " + tempToolkitDir + Environment.NewLine + "latestZipUri dlStatus: " + dlStatus.ToString() + Environment.NewLine + "latestZipPath: " + latestZipPath, "DPDM");
                //ExtractFile(latestZipPath, localToolkitDirRoot);
                //MessageBox.Show("Check ExtractFile destination: " + localToolkitDirRoot, "DPDM");
                //MergeXmlRepository(tempToolkitDir, newLocalToolkitDir);
                //MessageBox.Show("Check MergeXmlRepository destination: " + newLocalToolkitDir, "DPDM");
            }

            if (dlStatus == DownloadStatus.SUCCESS && File.Exists(latestZipPath))
            {
                // bulldoze the local process directory  
                if (args.Length == 2 && appExecFile.Equals(APP_UPDATING, StringComparison.InvariantCultureIgnoreCase))
                {
                    var lockedLocalFiles = DeleteDirectory(localToolkitDir);
                    if (lockedLocalFiles.Any())
                    {
                        ShowCurrentOperation("<WARNING> Local toolkit directory cleanup failed ...");
                        if (!ShowLockedFilesAndContinue(lockedLocalFiles))
                            Environment.Exit(1);
                    }
                }

                try
                {
                    // extract latest toolkit revision to the localToolkitDirRoot
                    // revised archive directory structure to be more like an installer
                    ExtractFile(latestZipPath, localToolkitDirRoot);

                    if (isDebugMe)
                        MessageBox.Show("Check unzipped files in: " + localToolkitDirRoot, "DPDM");
                }
                catch (Exception ex)
                {
                    if (DialogResult.No == MessageBox.Show("<ERROR> Could not unzip file: " + Path.GetFileName(latestZipPath) + Environment.NewLine +
                        "The AutoUpdater can not continue." + Environment.NewLine +
                        "Do you want to roll back the installation process?" + Environment.NewLine +
                        ex.Message, "RocksmithToolkit AutoUpdater", MessageBoxButtons.YesNo, MessageBoxIcon.Error))
                        Environment.Exit(1);

                    // rollback the process to its original state
                    DeleteDirectory(newLocalToolkitDir);
                    if (!Directory.Exists(localToolkitDir))
                        Directory.CreateDirectory(localToolkitDir);

                    RollBack(tempToolkitDir, localToolkitDir);
                    RestartToolkitGUI(localToolkitDir);
                }

                try
                {
                    // merge xml repo files
                    MergeXmlRepository(tempToolkitDir, newLocalToolkitDir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("<ERROR> Could not merge repositories ... " + Environment.NewLine + ex.Message, "RocksmithToolkit AutoUpdater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                // merge cgm inlay files
                var cgmFiles = Directory.EnumerateFiles(Path.Combine(tempToolkitDir, "cgm"), "*", SearchOption.AllDirectories);
                foreach (var cgmFile in cgmFiles)
                {
                    try
                    {
                        File.Copy(cgmFile, cgmFile.Replace(tempToolkitDir, newLocalToolkitDir));
                    }
                    catch {/* Do nothing */}
                }

                // TODO: merge custom/user dds xml/cfg files

                // cleanup tempToolkitDir
                var lockedTempFiles = DeleteDirectory(tempToolkitDir);
                if (lockedTempFiles.Any())
                {
                    ShowCurrentOperation("<WARNING> tempToolkitDir full cleanup failed ...");
                    if (!ShowLockedFilesAndContinue(lockedTempFiles))
                        Environment.Exit(1);
                }
            }

            if (File.Exists(latestZipPath))
                File.Delete(latestZipPath);

            if (localToolkitDir != newLocalToolkitDir)
            {
                // find open localToolkitDir and attempt to close it so it can be deleted
                var shellWindows = new SHDocVw.ShellWindows();
                foreach (SHDocVw.InternetExplorer shellWindow in shellWindows)
                {
                    var processType = Path.GetFileNameWithoutExtension(shellWindow.FullName).ToLower();
                    var cleanLocationUrl = shellWindow.LocationURL.ToLower().Replace(@"/", @"\");
                    if (processType.Equals("explorer") && cleanLocationUrl.Contains(localToolkitDir.ToLower()))
                    {
                        shellWindow.Quit();
                        Thread.Sleep(100);
                        shellWindow.Quit();
                        Thread.Sleep(100);

                        MessageBox.Show("The old local toolkit directory should now be closed: " + Environment.NewLine +
                                        localToolkitDir + Environment.NewLine + Environment.NewLine +
                                        "Manually close the directory if it is still open" + Environment.NewLine +
                                        "before pressing 'OK' to continue.  The old toolkit" + Environment.NewLine +
                                        "may be deleted after the update finishes ...", "Close Toolkit Directory ...", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                        break;
                    }
                }

                // attempt to delete the closed directory
                DeleteDirectory(localToolkitDir);
            }

            if (isDebugMe)
                MessageBox.Show("Before Restart: " + Environment.NewLine +
                                "Check localToolkitDir deleted: " + localToolkitDir + Environment.NewLine +
                                "Check tempToolkitDir deleted: " + tempToolkitDir + Environment.NewLine +
                                "Check latestZipPath deleted: " + latestZipPath, "DPDM");

            ShowCurrentOperation("Please wait ... Restarting ToolkitGUI ...");
            Thread.Sleep(1000); // settle down before restart
            RestartToolkitGUI(newLocalToolkitDir);
        }

        private void DownloadFile(Uri downloadUri, string destPath, int attempts = 4)
        {
            pbUpdate.Style = ProgressBarStyle.Continuous;
            pbUpdate.Refresh();

            for (int i = 0; i < attempts; i++)
            {
                dlStatus = DownloadStatus.WAIT;
                var webClient = new WebClient();
                webClient.DownloadFileCompleted += Completed;
                webClient.DownloadProgressChanged += ProgressChanged;

                sw.Start();
                webClient.DownloadFileAsync(downloadUri, destPath);

                while (dlStatus == DownloadStatus.WAIT)
                {
                    Application.DoEvents();
                    Thread.Sleep(100);
                }

                webClient.Dispose();

                if (dlStatus == DownloadStatus.SUCCESS || dlStatus == DownloadStatus.CANCEL)
                    return;
            }

            if (dlStatus == DownloadStatus.ERROR)
            {
                MessageBox.Show("<ERROR> Check internet connection ..." + Environment.NewLine +
                    lastError + Environment.NewLine +
                    "Make sure TLS 1.2 is enabled under 'Internet Options', 'Avanced' settings.  ",
                    "RocksmithToolkit AutoUpdater", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            // Update the progressbar percentage
            pbUpdate.Value = e.ProgressPercentage;

            // Show current operation and percentage status
            ShowCurrentOperation(String.Format("Downloading new version: {0}%", e.ProgressPercentage));

            // Calculate download speed
            lblSpeed.Text = String.Format("Speed: {0:0.00} kb/s", (e.BytesReceived / 1024d / sw.Elapsed.TotalSeconds));

            // Data have been downloaded so far and the total size of the file we are currently downloading
            lblDownloaded.Text = String.Format("Downloaded: {0:0.00} MB's / Total: {1:0.00} MB's", (e.BytesReceived / 1024d / 1024d), (e.TotalBytesToReceive / 1024d / 1024d));
        }

        private void Completed(object sender, AsyncCompletedEventArgs e)
        {
            // Reset the stopwatch.
            sw.Reset();

            if (e.Cancelled != true && e.Error == null)
            {
                dlStatus = DownloadStatus.SUCCESS;
                ShowCurrentOperation("Download was successful ...");
                pbUpdate.Style = ProgressBarStyle.Marquee;
                pbUpdate.Refresh();
            }
            else if (e.Error != null)
            {
                dlStatus = DownloadStatus.ERROR;
                lastError = e.Error.Message;
                ShowCurrentOperation("<ERROR> Check internet connection ...");
                Thread.Sleep(250);
            }
            else if (e.Cancelled == true)
            {
                dlStatus = DownloadStatus.CANCEL;
                ShowCurrentOperation("Download cancelled ...");
                MessageBox.Show("Download has been canceled.");
            }
            else
                dlStatus = DownloadStatus.UNKNOWN; // this should never happen
        }

        private void ExtractFile(string srcPath, string destDir)
        {
            // only works for zip files
            ShowCurrentOperation("Extracting: " + srcPath);
            AssemblyCaller.Call(Path.Combine(tempToolkitDir, APP_CSZIPLIB), "ICSharpCode.SharpZipLib.Zip.FastZip", "ExtractZip", new Type[] { typeof(string), typeof(string), typeof(string) }, new object[] { srcPath, destDir, null });
        }

        private void MergeXmlRepository(string srcDir, string destDir)
        {
            ShowCurrentOperation("Preparing to merge repositories ...");

            // this check must happen after download has been unzipped to the newLocalToolkitPath
            try
            {
                bool? replaceRepo = null;
                // get old replace repo from old ConfigRepo
                replaceRepo = (bool)AssemblyCaller.CallStatic(Path.Combine(appExecDir, APP_RSLIB), "RocksmithToolkitLib.XmlRepository.ConfigRepository", "GetBoolean", "general_replacerepo");
                // get old config version from old ConfigRepo
                var xmlConfigVersion = (string)AssemblyCaller.CallStatic(Path.Combine(appExecDir, APP_RSLIB), "RocksmithToolkitLib.XmlRepository.ConfigRepository", "GetString", "general_configversion");
                // get new config version from new GeneralConfig
                var generalConfigVersion = (string)AssemblyCaller.CallStatic(Path.Combine(appExecDir, APP_RSGUI), "RocksmithToolkitGUI.Config.GeneralConfig", "GetConfigVersion", null);

                if (String.IsNullOrEmpty(xmlConfigVersion))
                    replaceRepo = true;
                else if (String.IsNullOrEmpty(generalConfigVersion))
                    replaceRepo = true;
                else if (xmlConfigVersion != generalConfigVersion) // compare versions
                    replaceRepo = true;
                else if (replaceRepo == null)
                    replaceRepo = true;

                if (replaceRepo == true)
                {
                    if (isDebugMe)
                        MessageBox.Show("XmlRepositories were not merged ..." + Environment.NewLine +
                            "replaceRepo = " + replaceRepo.ToString() + Environment.NewLine +
                            "xmlConfigVersion = " + xmlConfigVersion + Environment.NewLine +
                            "generalConfigVersion = " + generalConfigVersion, "DEBUG ME");

                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("<WARNING> XmlRepositories could not be merged ..." + Environment.NewLine +
                    ex.Message, "RocksmithToolkit AutoUpdater", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return;
            }

            ShowCurrentOperation("Merging repositories ...");
            var repositories = new Dictionary<string, string>()
                {
                    {"ConfigRepository", REPO_CONFIG},
                    {"SongAppIdRepository", REPO_SONGAPPID}, 
                    {"TuningDefinitionRepository", REPO_TUNINGDEF}
                };

            foreach (KeyValuePair<string, string> repo in repositories)
            {
                var srcPath = Path.Combine(srcDir, repo.Value);
                var destPath = Path.Combine(destDir, repo.Value);

                // merge if srcPath and destPath exist 
                if (File.Exists(srcPath) && File.Exists(destPath))
                {
                    try
                    {
                        AssemblyCaller.CallStatic(Path.Combine(appExecDir, APP_RSLIB), String.Format("RocksmithToolkitLib.XmlRepository.{0}", repo.Key), "Merge", srcPath, destPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("<ERROR> MergeXmlRepository file, AssemblyCaller failure ..." + Environment.NewLine +
                            "srcPath = " + srcPath + Environment.NewLine +
                            "destPath = " + destPath + Environment.NewLine +
                            ex.InnerException.Message, "RocksmithToolkit AutoUpdater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void RestartToolkitGUI(string toolkitDir)
        {
            if (isDebugMe)
                MessageBox.Show("Attempting to RestartToolkitGUI: " + Environment.NewLine +
                                Path.Combine(toolkitDir, APP_RSGUI));

            Process updaterProcess = new Process();
            updaterProcess.StartInfo.FileName = Path.Combine(toolkitDir, APP_RSGUI);
            updaterProcess.StartInfo.WorkingDirectory = toolkitDir;
            updaterProcess.Start();

            Environment.Exit(0);
        }

        private void ShowCurrentOperation(string message)
        {
            Helpers.InvokeIfRequired(this, delegate
            {
                lblCurrentOperation.Text = message;
                lblCurrentOperation.Refresh();
            });
        }

        private bool BackupProcessDir(string srcDir, string destDir)
        {
            // make a backup copy of the process directory for rollback
            ShowCurrentOperation("Backing up process ...");
            var lockedFiles = CopyDirectory(srcDir, destDir);
            if (lockedFiles.Any())
            {
                ShowCurrentOperation("<ERROR> Backup failed ...");
                if (!ShowLockedFilesAndContinue(lockedFiles))
                    return false;

                return true;
            }

            ShowCurrentOperation("Backup was sucessful ...");
            return true;
        }

        private List<string> CopyDirectory(string srcDir, string destDir, List<string> ignoreFiles = null)
        {
            var lockedFiles = new List<string>();
            if (ignoreFiles == null)
                ignoreFiles = new List<string>();

            // create backup root directory
            var rootDir = srcDir.Replace(srcDir, destDir);
            if (!Directory.Exists(rootDir))
                Directory.CreateDirectory(rootDir);

            if (isDebugMe) MessageBox.Show("CopyDirectory rootDir = " + rootDir, "DPDM");

            // create backup subdirectories
            var dirPaths = Directory.EnumerateDirectories(srcDir, "*", SearchOption.AllDirectories);
            foreach (var dirPath in dirPaths)
            {
                var subDir = dirPath.Replace(srcDir, destDir);
                if (!Directory.Exists(subDir))
                    Directory.CreateDirectory(subDir);

                if (isDebugMe) MessageBox.Show("CopyDirectory subDir = " + subDir, "DPDM");
            }

            // copy all files
            var filePaths = Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories);
            // create some progress bar movement
            var step = (int)Math.Floor(100.0 / filePaths.Count());
            int progress = 0;

            foreach (string filePath in filePaths)
            {
                progress += step;
                pbUpdate.Value = progress;

                var isIgnored = false;
                foreach (var ignoreFile in ignoreFiles)
                    if (filePath == ignoreFile)
                    {
                        isIgnored = true;
                        break;
                    }

                if (isIgnored)
                    continue;

                var destFilePath = filePath.Replace(srcDir, destDir);

                try
                {
                    ShowCurrentOperation("Copying: " + Path.GetFileName(filePath));
                    File.SetAttributes(filePath, FileAttributes.Normal);
                    File.Copy(filePath, destFilePath, true);
                }
                catch
                {
                    lockedFiles.Add(destFilePath);
                }
            }

            return lockedFiles;
        }

        /// <summary>
        /// This is a permenant delete.  Double check the srcDir is set correctly.
        /// </summary>
        /// <param name="srcDir"></param>
        /// <returns></returns>
        private List<string> DeleteDirectory(string srcDir)
        {
            var lockedFiles = new List<string>();
            if (!Directory.Exists(srcDir))
                return lockedFiles;

            if (isInDesignMode)
            {
                if (DialogResult.Yes != MessageBox.Show("<WARNING> About to permenantly delete srcDir: " + Environment.NewLine +
                    srcDir + Environment.NewLine +
                    "Including all files, subdirectories, and the srcDir path too!" + Environment.NewLine +
                    "Are you sure you want to continue?", "<WARNING> README ...", MessageBoxButtons.YesNo, MessageBoxIcon.Warning))
                {
                    lockedFiles.Add("DeleteDirectory was cancelled by developer ...");
                    return lockedFiles;
                }
            }

            var filePaths = Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories);
            // create some progress bar movement
            var step = (int)Math.Floor(100.0 / filePaths.Count());
            int progress = 0;

            foreach (var filePath in filePaths)
            {
                progress += step;
                pbUpdate.Value = progress;

                try
                {
                    if (File.Exists(filePath))
                    {
                        ShowCurrentOperation("Deleting: " + Path.GetFileName(filePath));
                        File.SetAttributes(filePath, FileAttributes.Normal);
                        File.Delete(filePath);
                    }
                }
                catch
                {
                    lockedFiles.Add(filePath);
                }
            }

            var directories = Directory.EnumerateDirectories(srcDir, "*", SearchOption.AllDirectories);
            foreach (var dir in directories)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir);
                }
                catch
                {
                    lockedFiles.Add(dir);
                }
            }

            return lockedFiles;
        }

        private void RollBack(string srcDir, string destDir)
        {
            ShowCurrentOperation("Rolling back original installation ...");

            var updaterPath = Path.Combine(srcDir, appExecFile.Equals(APP_UPDATER, StringComparison.InvariantCultureIgnoreCase) ? APP_UPDATER : APP_UPDATING);
            var ignoreFiles = new List<string>() { updaterPath, latestZipPath };
            var lockedDestFiles = DeleteDirectory(destDir);
            var lockedSrcFiles = CopyDirectory(srcDir, destDir, ignoreFiles);
            var lockedFiles = new List<string>();
            lockedFiles.AddRange(lockedDestFiles);
            lockedFiles.AddRange(lockedSrcFiles);

            if (lockedFiles.Any())
            {
                ShowCurrentOperation("<ERROR> Rollback failed, locked files ...");
                if (!ShowLockedFilesAndContinue(lockedFiles))
                    Environment.Exit(0);
            }
        }

        private bool ShowLockedFilesAndContinue(List<string> lockedFiles)
        {
            if (!lockedFiles.Any())
                return true;

            var sb = new StringBuilder();
            sb.AppendLine("<WARNING> Some files could not be programmatically deleted!");
            sb.AppendLine("Please remember to deleted these after auto update is finished ...  ");
            sb.AppendLine("");

            foreach (var lockedFile in lockedFiles)
                sb.AppendLine(lockedFile);

            sb.AppendLine();
            sb.AppendLine("Do you want continue?");

            if (DialogResult.OK != MessageBox.Show(sb.ToString(), "RocksmithToolkit AutoUpdater",
                 MessageBoxButtons.OKCancel, MessageBoxIcon.Error))
                return false;

            return true;
        }

        private void AutoUpdaterForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !isInDesignMode)
            {
                // cleanly cancell download if active
                if (webClient != null && webClient.IsBusy)
                {
                    webClient.CancelAsync();
                    webClient.Dispose();
                    webClient = null;
                }

                // always let AutoUpdater exit programatically
                dlStatus = DownloadStatus.CANCEL;
                e.Cancel = true;
            }
        }


    }
}
