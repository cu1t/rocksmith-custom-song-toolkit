﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.ComponentModel;
using System.IO;
using Ookii.Dialogs;
using RocksmithToolkitLib.DLCPackage;
using RocksmithToolkitLib;
using RocksmithToolkitLib.DLCPackage.AggregateGraph2014;
using RocksmithToolkitLib.Extensions;
using RocksmithToolkitLib.Sng;
using RocksmithToolkitLib.XML;
using RocksmithToolkitLib.XmlRepository;
using RocksmithToolkitGUI.Config;
using RocksmithToolkitLib.PsarcLoader;
using PackageCreator = RocksmithToolkitLib.DLCPackage.DLCPackageCreator;

namespace RocksmithToolkitGUI.DLCPackerUnpacker
{
    public partial class DLCPackerUnpacker : UserControl
    {
        private const string MESSAGEBOX_CAPTION = "CDLC Packer/Unpacker";
        private const string TKI_APPID = "(AppID by Packer/Unpacker)";
        private BackgroundWorker bwRepack = new BackgroundWorker();
        private string destPath;
        private StringBuilder errorsFound;

        public DLCPackerUnpacker()
        {
            InitializeComponent();

            try
            {
                PopulateGameVersionCombo();
            }
            catch { /*For mono compatibility*/ }

            // AppID updater worker
            bwRepack.DoWork += new DoWorkEventHandler(UpdateAppId);
            bwRepack.ProgressChanged += new ProgressChangedEventHandler(ProgressChanged);
            bwRepack.RunWorkerCompleted += new RunWorkerCompletedEventHandler(ProcessCompleted);
            bwRepack.WorkerReportsProgress = true;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // perma fix to prevent creating a property value in designer
        public bool DecodeAudio
        {
            get { return chkDecodeAudio.Checked; }
            set { chkDecodeAudio.Checked = value; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // perma fix to prevent creating a property value in designer
        public bool OverwriteSongXml
        {
            get { return chkOverwriteSongXml.Checked; }
            set { chkOverwriteSongXml.Checked = value; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // perma fix to prevent creating a property value in designer
        public bool UpdateManifest
        {
            get { return chkUpdateManifest.Checked; }
            set { chkUpdateManifest.Checked = value; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // perma fix to prevent creating a property value in designer
        public bool UpdateSng
        {
            get { return chkUpdateSng.Checked; }
            set { chkUpdateSng.Checked = value; }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // perma fix to prevent creating a property value in designer
        public string NewAppId
        {
            get { return txtAppId.Text; }
            set { txtAppId.Text = value.GetValidAppIdSixDigits(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // perma fix to prevent creating a property value in designer
        public GameVersion Version { get; set; }

        private void PopulateGameVersionCombo()
        {
            var gameVersionList = Enum.GetNames(typeof(GameVersion)).ToList<string>();
            gameVersionList.Remove("None");
            cmbGameVersion.Items.Clear();
            foreach (var version in gameVersionList)
                cmbGameVersion.Items.Add(version);

            cmbGameVersion.SelectedItem = ConfigRepository.Instance()["general_defaultgameversion"];
        }

        private void PopulateAppIdCombo(GameVersion gameVersion)
        {
            cmbAppId.Items.Clear();
            foreach (var song in SongAppIdRepository.Instance().Select(gameVersion))
                cmbAppId.Items.Add(song);

            var songAppId = SongAppIdRepository.Instance().Select((gameVersion == GameVersion.RS2014) ? ConfigRepository.Instance()["general_defaultappid_RS2014"] : ConfigRepository.Instance()["general_defaultappid_RS2012"], gameVersion);
            cmbAppId.SelectedItem = songAppId;
            NewAppId = songAppId.AppId;
        }

        private void PromptComplete(string destDirPath, bool actionPacking = true, string errMsg = null)
        {
            if (actionPacking)
                destDirPath = Path.GetDirectoryName(destDirPath);

            var actionMsg = "Packing";
            if (!actionPacking)
                actionMsg = "Unpacking";

            if (String.IsNullOrEmpty(errMsg))
                errMsg = actionMsg + " is complete." + Environment.NewLine;
            else
                errMsg = actionMsg + " is complete with the following errors:" + Environment.NewLine +
                         errMsg + Environment.NewLine;

            errMsg += "Would you like to open the destination path?  ";

            if (MessageBox.Show(new Form { TopMost = true }, errMsg, MESSAGEBOX_CAPTION, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(destDirPath);
        }


        public void SelectComboAppId(string appId)
        {
            var songAppId = SongAppIdRepository.Instance().Select(appId, Version);
            if (SongAppIdRepository.Instance().List.Any<SongAppId>(a => a.AppId == appId))
                cmbAppId.SelectedItem = songAppId;
            else
            {
                if (!appId.IsAppIdSixDigits())
                    MessageBox.Show("Please enter a valid six digit  " + Environment.NewLine + "App ID before continuing.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Stop);
                else
                    MessageBox.Show("User entered an unknown AppID." + Environment.NewLine + Environment.NewLine + "Toolkit will use the AppID that  " + Environment.NewLine + "was entered manually but it can  " + Environment.NewLine + "not assess its validity.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ShowCurrentOperation(string message)
        {
            lblCurrentOperation.Text = message;
            lblCurrentOperation.Refresh();
        }

        private void ToggleUIControls(bool enable)
        {
            picLogo.Select();
            picLogo.Focus();
            txtAppId.Enabled = enable;
            btnFixLowBassTuning.Enabled = enable;
            btnPack.Enabled = enable;
            btnPackSongPack.Enabled = enable;
            btnAppIdSong.Enabled = enable;
            btnAppIdFolder.Enabled = enable;
            btnSelectSongs.Enabled = enable;
            btnUnpack.Enabled = enable;
            chkDecodeAudio.Enabled = enable;
            chkDeleteSourceFile.Enabled = enable;
            chkOverwriteSongXml.Enabled = enable;
            chkQuickBassFix.Enabled = enable;
            chkVerbose.Enabled = enable;
            chkUpdateSng.Enabled = enable;
            chkUpdateManifest.Enabled = enable;
        }

        public List<string> UnpackSongs(IEnumerable<string> srcPaths, string destPath)
        {
            ToggleUIControls(false);
            errorsFound = new StringBuilder();
            GlobalExtension.UpdateProgress = this.pbUpdateProgress;
            GlobalExtension.CurrentOperationLabel = this.lblCurrentOperation;
            Thread.Sleep(100); // give Globals a chance to initialize

            var unpackedDirs = new List<string>();
            var structured = ConfigRepository.Instance().GetBoolean("creator_structured");
            var step = (int)Math.Floor(100.0 / (srcPaths.Count() * 2)); // Math.Floor prevents roundup errors
            int progress = 0;
            Stopwatch sw = new Stopwatch();
            sw.Restart();

            foreach (string srcPath in srcPaths)
            {
                Application.DoEvents();
                progress += step;
                GlobalExtension.ShowProgress(String.Format("Unpacking: '{0}'", Path.GetFileName(srcPath)), progress);

                try
                {
                    Platform srcPlatform = srcPath.GetPlatform();
                    var unpackedDir = Packer.Unpack(srcPath, destPath, srcPlatform, DecodeAudio, OverwriteSongXml);

                    // added a bulk process to create template xml files here so unpacked folders may be loaded quickly in CDLC Creator if desired
                    progress += step;
                    GlobalExtension.ShowProgress(String.Format("Creating Template XML file for: '{0}'", Path.GetFileName(srcPath)), progress);
                    using (var packageCreator = new DLCPackageCreator.DLCPackageCreator())
                    {
                        DLCPackageData info = null;
                        if (srcPlatform.version == GameVersion.RS2014)
                            info = DLCPackageData.LoadFromFolder(unpackedDir, srcPlatform, srcPlatform, true, true);
                        else
                            info = DLCPackageData.RS1LoadFromFolder(unpackedDir, srcPlatform, false);

                        info.GameVersion = srcPlatform.version;

                        switch (srcPlatform.platform)
                        {
                            case GamePlatform.Pc:
                                info.Pc = true;
                                break;
                            case GamePlatform.Mac:
                                info.Mac = true;
                                break;
                            case GamePlatform.XBox360:
                                info.XBox360 = true;
                                break;
                            case GamePlatform.PS3:
                                info.PS3 = true;
                                break;
                        }

                        packageCreator.FillPackageCreatorForm(info, unpackedDir);
                        // fix descrepancies
                        packageCreator.CurrentGameVersion = srcPlatform.version;
                        // console files do not have an AppId
                        if (!srcPlatform.IsConsole)
                            packageCreator.AppId = info.AppId;
                        //packageCreator.SelectComboAppId(info.AppId);
                        // save template xml file except when SongPack
                        if (!srcPath.Contains("_sp_") && !srcPath.Contains("_songpack_"))
                            packageCreator.SaveTemplateFile(unpackedDir, false);

                    }

                    unpackedDirs.Add(unpackedDir);
                }
                catch (Exception ex)
                {
                    // ignore any 'Index out of range' exceptions
                    if (!ex.Message.StartsWith("Index"))
                        errorsFound.AppendLine(String.Format("<ERROR> Unpacking file: '{0}' ... {1}", Path.GetFileName(srcPath), ex.Message));
                }
            }

            sw.Stop();
            GlobalExtension.ShowProgress("Finished unpacking archive (elapsed time): " + sw.Elapsed, 100);

            if (!ConfigGlobals.IsUnitTest)
                PromptComplete(destPath, false, errorsFound.ToString());

            GlobalExtension.Dispose();
            ToggleUIControls(true);

            return unpackedDirs;
        }

        private void ProcessCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            switch (Convert.ToString(e.Result))
            {
                case "repack":
                    if (errorsFound.Length <= 0)
                        MessageBox.Show("App ID update is complete.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show("App ID update is complete with errors. See below: " + Environment.NewLine + errorsFound.ToString(), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    break;
                case "unpack":
                    if (errorsFound.Length <= 0)
                        MessageBox.Show("Unpacking is complete.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show("Unpacking is complete with errors. See below: " + Environment.NewLine + Environment.NewLine + errorsFound.ToString(), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    break;
            }

            ToggleUIControls(true);
            pbUpdateProgress.Visible = false;
            lblCurrentOperation.Visible = false;
        }

        private void ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage <= pbUpdateProgress.Maximum)
                pbUpdateProgress.Value = e.ProgressPercentage;
            else
                pbUpdateProgress.Value = pbUpdateProgress.Maximum;

            ShowCurrentOperation(e.UserState as string);
        }

        public void UpdateAppId(object sender, DoWorkEventArgs e)
        {
            var srcFilePaths = e.Argument as string[];
            errorsFound = new StringBuilder();
            var step = (int)Math.Round(1.0 / srcFilePaths.Length * 100, 0);
            int progress = 0;

            // show some initial progress if only one song
            if (step > 99) progress = 50;

            var tmpDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;
            var appId = NewAppId;
            if (String.IsNullOrEmpty(appId))
                throw new InvalidDataException("<ERROR> AppID is null or empty ...");

            foreach (string srcFilePath in srcFilePaths)
            {
                Application.DoEvents();
                var srcPlatform = srcFilePath.GetPlatform();
                bwRepack.ReportProgress(progress, String.Format("Updating '{0}'", Path.GetFileName(srcFilePath)));

                if (!srcPlatform.IsConsole)
                {
                    NoCloseStream dataStream = new NoCloseStream();
                    try
                    {
                        // use fast PsarcLoader memory methods (respect processing order/grouping)
                        using (PSARC p = new PSARC(true))
                        {
                            // write the new appid.appid
                            using (var fs = File.OpenRead(srcFilePath))
                                p.Read(fs);

                            dataStream = p.ReplaceData(x => x.Name.Equals("appid.appid"), appId);

                            using (var fs = File.Create(srcFilePath))
                                p.Write(fs, true);

                            // update toolkit.version
                            var tkStream = p.GetData(x => x.Name.Equals("toolkit.version"));
                            if (tkStream != null)
                            {
                                using (var tkReader = new StreamReader(tkStream))
                                {
                                    var tkInfo = GeneralExtension.GetToolkitInfo(tkReader);
                                    var packageComment = tkInfo.PackageComment;
                                    if (String.IsNullOrEmpty(packageComment))
                                        packageComment = TKI_APPID;
                                    else if (!packageComment.Contains(TKI_APPID))
                                        packageComment = packageComment + " " + TKI_APPID;

                                    var toolkitVersion = ToolkitVersion.RSTKGuiVersion;
                                    if (!tkInfo.ToolkitVersion.Contains(toolkitVersion))
                                        toolkitVersion = String.Format("{0} ({1})", toolkitVersion, tkInfo.ToolkitVersion);

                                    using (var tkInfoStream = new MemoryStream())
                                    {
                                        PackageCreator.GenerateToolkitVersion(tkInfoStream, tkInfo.PackageAuthor, tkInfo.PackageVersion, packageComment, tkInfo.PackageRating, toolkitVersion);
                                        PsarcExtensions.InjectArchiveEntry(srcFilePath, "toolkit.version", tkInfoStream);
                                        tkInfoStream.Dispose(); // CRITICAL
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errorsFound.AppendLine(String.Format("Error trying repack file '{0}': {1}", Path.GetFileName(srcFilePath), ex.Message));
                    }

                    if (dataStream != null)
                        dataStream.CloseEx();

                    progress += step;
                    bwRepack.ReportProgress(progress);
                }
                else
                    errorsFound.AppendLine(String.Format("File '{0}' is not a valid desktop platform package.", Path.GetFileName(srcFilePath)));
            }

            bwRepack.ReportProgress(100);
            e.Result = "repack";
        }

        private void btnFixLowBassTuning_Click(object sender, EventArgs e)
        {
            string[] srcPaths;
            bool alreadyFixed;
            bool hasBass;

            // GET PATH
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select the CDLC(s) which to apply Bass Tuning Fix";
                ofd.Filter = "All Files (*.*)|*.*|Rocksmith 2014 PC|*_p.psarc|Rocksmith 2014 Mac|*_m.psarc|Rocksmith 2014 Xbox|*_xbox|Rocksmith 2014 PS3|*.edat";
                ofd.Multiselect = true;
                ofd.FileName = destPath;

                if (ofd.ShowDialog() != DialogResult.OK) return;

                srcPaths = ofd.FileNames;
            }

            var fixLowBass = ConfigRepository.Instance().GetBoolean("creator_fixlowbass");

            ToggleUIControls(false);
            errorsFound = new StringBuilder();
            GlobalExtension.UpdateProgress = this.pbUpdateProgress;
            GlobalExtension.CurrentOperationLabel = this.lblCurrentOperation;
            Thread.Sleep(100); // give Globals a chance to initialize
            Stopwatch sw = new Stopwatch();
            sw.Restart();

            foreach (var srcPath in srcPaths)
            {
                // UNPACK
                var packagePlatform = srcPath.GetPlatform();
                var tmpPath = Path.GetTempPath();
                Application.DoEvents();

                string unpackedDir;
                try
                {
                    unpackedDir = Packer.Unpack(srcPath, tmpPath, overwriteSongXml: true, predefinedPlatform: packagePlatform);
                }
                catch (Exception ex)
                {
                    errorsFound.AppendLine(String.Format("Error trying unpack file '{0}': {1}", Path.GetFileName(srcPath), ex.Message));
                    continue;
                }

                destPath = Path.Combine(Path.GetDirectoryName(srcPaths[0]), Path.GetFileName(unpackedDir));

                GlobalExtension.ShowProgress(String.Format("Loading '{0}' ...", Path.GetFileName(srcPath)), 40);

                // Same name xbox issue fix
                //if (packagePlatform.platform == GamePlatform.XBox360)
                //    destPath = String.Format("{0}_{1}", destPath, GamePlatform.XBox360.ToString());

                IOExtension.MoveDirectory(unpackedDir, destPath, true);
                unpackedDir = destPath;

                // Low Bass Tuning Fix is for Rocksmith 2014 Only
                packagePlatform = new Platform(packagePlatform.platform, GameVersion.RS2014);
                // LOAD DATA
                var info = DLCPackageData.LoadFromFolder(unpackedDir, packagePlatform, packagePlatform, false);

                switch (packagePlatform.platform)
                {
                    case GamePlatform.Pc:
                        info.Pc = true;
                        break;
                    case GamePlatform.Mac:
                        info.Mac = true;
                        break;
                    case GamePlatform.XBox360:
                        info.XBox360 = true;
                        break;
                    case GamePlatform.PS3:
                        info.PS3 = true;
                        break;
                }

                //apply bass fix
                GlobalExtension.ShowProgress(String.Format("Applying Bass Tuning Fix '{0}' ...", Path.GetFileName(srcPath)), 60);
                alreadyFixed = false;
                hasBass = false;

                for (int i = 0; i < info.Arrangements.Count; i++)
                {
                    Arrangement arr = info.Arrangements[i];
                    if (arr.ArrangementType == ArrangementType.Bass)
                    {
                        hasBass = true;

                        if (arr.TuningStrings.String0 < -4 && arr.TuningPitch != 220.0)
                        {
                            if (!TuningFrequency.ApplyBassFix(arr, fixLowBass))
                            {
                                if (chkVerbose.Checked)
                                    MessageBox.Show(Path.GetFileName(srcPath) + "  " + Environment.NewLine + "bass arrangement is already at 220Hz pitch.  ", "Error ... Applying Low Bass Tuning Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);

                                alreadyFixed = true;
                            }
                        }
                        else
                        {
                            if (chkVerbose.Checked)
                                MessageBox.Show(Path.GetFileName(srcPath) + "  " + Environment.NewLine + "bass arrangement tuning does not need to be fixed.  ", "Error ... Applying Low Bass Tuning Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);

                            alreadyFixed = true;
                        }
                    }
                }

                // don't repackage a song that is already fixed or doesn't have bass
                if (alreadyFixed || !hasBass)
                {
                    if (chkVerbose.Checked && !hasBass)
                        MessageBox.Show(Path.GetFileName(srcPath) + "  " + Environment.NewLine + "has no bass arrangement.  ", "Error ... Applying Low Bass Tuning Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    IOExtension.DeleteDirectory(unpackedDir);
                    continue;
                }

                var ndx = srcPath.LastIndexOf('_');
                var srcName = srcPath.Substring(0, ndx);
                var srcExt = srcPath.Substring(ndx, srcPath.Length - ndx);

                if (!chkQuickBassFix.Checked)
                {
                    using (var ofd = new SaveFileDialog())
                    {
                        ofd.Title = "Select a name for the Low Bass Tuning Fixed file.";
                        ofd.Filter = "All Files (*.*)|*.*|Rocksmith 2014 PC|*_p.psarc|Rocksmith 2014 Mac|*_m.psarc|Rocksmith 2014 Xbox|*_xbox|Rocksmith 2014 PS3|*.edat";
                        ofd.FileName = String.Format("{0}_{1}_bassfix{2}", info.SongInfo.ArtistSort, info.SongInfo.SongDisplayNameSort, srcExt);

                        if (ofd.ShowDialog() != DialogResult.OK)
                            return;

                        destPath = ofd.FileName;
                    }
                }
                else
                    destPath = String.Format("{0}_bassfix{1}", srcName, srcExt);

                if (Path.GetFileName(destPath).Contains(" ") && info.PS3)
                    if (!ConfigRepository.Instance().GetBoolean("creator_ps3pkgnamewarn"))
                        MessageBox.Show(String.Format("PS3 package name can't support space character due to encryption limitation. {0} Spaces will be automatic removed for your PS3 package name.", Environment.NewLine), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else
                        ConfigRepository.Instance()["creator_ps3pkgnamewarn"] = true.ToString();

                if (chkDeleteSourceFile.Checked)
                {
                    try
                    {
                        File.Delete(srcPath);
                    }
                    catch (Exception ex)
                    {
                        Console.Write(ex.Message);
                        MessageBox.Show("Access rights required to delete source package, or an error occurred. Package still may exist. Try running as Administrator.");
                    }
                }

                // Generate Fixed Low Bass Tuning Package
                GlobalExtension.ShowProgress(String.Format("Repackaging '{0}' ...", Path.GetFileName(srcPath)), 80);
                // TODO consider user of regular packer here
                RocksmithToolkitLib.DLCPackage.DLCPackageCreator.Generate(destPath, info, packagePlatform);

                if (!GeneralExtension.IsInDesignMode)
                    IOExtension.DeleteDirectory(unpackedDir);
            }

            sw.Stop();
            GlobalExtension.ShowProgress("Finished applying low bass tuning fix (elapsed time): " + sw.Elapsed, 100);
            if (String.IsNullOrEmpty(destPath))
                destPath = srcPaths[0];

            PromptComplete(destPath, errMsg: errorsFound.ToString());
            GlobalExtension.Dispose();
            ToggleUIControls(true);
        }

        private void btnPackSongPack_Click(object sender, EventArgs e)
        {
            var srcPath = String.Empty;
            var errMsg = String.Empty;

            using (var fbd = new VistaFolderBrowserDialog())
            {
                fbd.Description = "Select the Song Pack folder created in Step #1.";
                fbd.SelectedPath = destPath;

                if (fbd.ShowDialog() != DialogResult.OK)
                    return;

                srcPath = fbd.SelectedPath;
            }

            ToggleUIControls(false);
            GlobalExtension.UpdateProgress = this.pbUpdateProgress;
            GlobalExtension.CurrentOperationLabel = this.lblCurrentOperation;
            Thread.Sleep(100); // give Globals a chance to initialize
            GlobalExtension.ShowProgress("Packing archive ...", 30);
            Application.DoEvents();

            try
            {
                Stopwatch sw = new Stopwatch();
                sw.Restart();

                var songPackDir = AggregateGraph2014.DoLikeSongPack(srcPath, NewAppId);
                destPath = Path.Combine(Path.GetDirectoryName(srcPath), String.Format("{0}_songpack_p.psarc", Path.GetFileName(srcPath)));
                // PC Only for now can't mix platform packages
                Packer.Pack(songPackDir, destPath, predefinedPlatform: new Platform(GamePlatform.Pc, GameVersion.RS2014));

                // clean up now (song pack folder)
                if (Directory.Exists(songPackDir))
                    IOExtension.DeleteDirectory(songPackDir);

                sw.Stop();
                GlobalExtension.ShowProgress("Finished packing archive (elapsed time): " + sw.Elapsed, 100);
            }
            catch (Exception ex)
            {
                errMsg = String.Format("{0}\n{1}", ex.Message, ex.InnerException);
                errMsg += Environment.NewLine + "Make sure there aren't any non-PC CDLC in the SongPacks folder.";
            }

            PromptComplete(destPath, true, errMsg);
            GlobalExtension.Dispose();
            ToggleUIControls(true);
        }

        private void btnPack_Click(object sender, EventArgs e)
        {
            var srcPath = String.Empty;
            var destPath = String.Empty;

            using (var fbd = new VistaFolderBrowserDialog())
            {
                fbd.SelectedPath = destPath;
                fbd.Description = "Select CDLC artifacts folder.";

                if (fbd.ShowDialog() != DialogResult.OK)
                    return;
                srcPath = destPath = fbd.SelectedPath;
            }

            destPath = Packer.RecycleUnpackedDir(srcPath);

            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Select a new CDLC destination file name or use the system generated default.";
                sfd.FileName = destPath;

                if (sfd.ShowDialog() != DialogResult.OK)
                    return;
                destPath = sfd.FileName;
            }

            PackSong(srcPath, destPath);
        }

        public string PackSong(string srcPath, string destPath)
        {
            ToggleUIControls(false);
            GlobalExtension.UpdateProgress = this.pbUpdateProgress;
            GlobalExtension.CurrentOperationLabel = this.lblCurrentOperation;
            Thread.Sleep(100); // give Globals a chance to initialize
            GlobalExtension.ShowProgress("Packing archive ...", 30);
            Application.DoEvents();
            var errMsg = String.Empty;
            var archivePath = String.Empty;

            try
            {
                Stopwatch sw = new Stopwatch();
                sw.Restart();
                var srcPlatform = srcPath.GetPlatform();
                archivePath = Packer.Pack(srcPath, destPath, srcPlatform, UpdateSng, UpdateManifest);
                sw.Stop();
                GlobalExtension.ShowProgress("Finished packing archive (elapsed time): " + sw.Elapsed, 100);
            }
            catch (Exception ex)
            {
                errMsg = String.Format("{0}\n{1}", ex.Message, ex.InnerException);
            }

            if (!ConfigGlobals.IsUnitTest)
                PromptComplete(destPath, true, errMsg);

            GlobalExtension.Dispose();
            ToggleUIControls(true);

            return archivePath;
        }

        private void btnRepackAppId_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Multiselect = true;

                if ((GameVersion)Enum.Parse(typeof(GameVersion), cmbGameVersion.SelectedItem.ToString()) == GameVersion.RS2012)
                    ofd.Filter = "Custom Rocksmith CDLC (*.dat)|*.dat";
                else
                    ofd.Filter = "Custom Rocksmith 2014 CDLC (*.psarc)|*.psarc";

                ofd.Title = "Select one or more CDLC files to update";
                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                if (!bwRepack.IsBusy && ofd.FileNames.Length > 0)
                {
                    pbUpdateProgress.Value = 0;
                    pbUpdateProgress.Visible = true;
                    lblCurrentOperation.Visible = true;
                    ToggleUIControls(false);
                    bwRepack.RunWorkerAsync(ofd.FileNames);
                }
            }
        }

        private void btnRepackFolder_Click(object sender, EventArgs e)
        {
            using (var fbd = new VistaFolderBrowserDialog())
            {
                string filter;

                if ((GameVersion)Enum.Parse(typeof(GameVersion), cmbGameVersion.SelectedItem.ToString()) == GameVersion.RS2012)
                    filter = "*.dat";
                else
                    filter = "*.psarc";

                fbd.Description = "Select a CDLC folder to update";
                fbd.SelectedPath = destPath;
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;

                var files = Directory.EnumerateFiles(fbd.SelectedPath, filter, SearchOption.AllDirectories).ToArray();

                if (!bwRepack.IsBusy && files.Length > 0)
                {
                    pbUpdateProgress.Value = 0;
                    pbUpdateProgress.Visible = true;
                    lblCurrentOperation.Visible = true;
                    ToggleUIControls(false);
                    bwRepack.RunWorkerAsync(files);
                }
            }
        }

        private void btnSelectSongs_Click(object sender, EventArgs e)
        {
            string[] srcPaths;
            destPath = String.Empty;

            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select multiple songs to include in Song Pack";
                ofd.Filter = "Rocksmith 2014 PC Only|*_p.psarc";
                ofd.FilterIndex = 1;
                ofd.Multiselect = true;

                if (ofd.ShowDialog() != DialogResult.OK)
                    return;
                srcPaths = ofd.FileNames;
            }

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select 'Make New Folder' then right click to 'Rename'" + Environment.NewLine + "the 'New Folder' to the desired Song Pack name." + Environment.NewLine + "Make sure the new Song Pack folder stays selected then click Ok.";
                fbd.SelectedPath = Path.GetDirectoryName(srcPaths[0]) + Path.DirectorySeparatorChar;

                if (fbd.ShowDialog() != DialogResult.OK)
                    return;
                destPath = fbd.SelectedPath;
            }

            UnpackSongs(srcPaths, destPath);
        }

        private void btnUnpack_Click(object sender, EventArgs e)
        {
            string[] srcPaths;

            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "All Files (*.*)|*.*|Rocksmith 1|*.dat|Rocksmith 2014 PC|*_p.psarc|Rocksmith 2014 Mac|*_m.psarc|Rocksmith 2014 Xbox|*_xbox|Rocksmith 2014 PS3|*.edat";
                ofd.Multiselect = true;
                ofd.FilterIndex = 1;
                ofd.FileName = destPath;

                if (ofd.ShowDialog() != DialogResult.OK)
                    return;
                srcPaths = ofd.FileNames;
            }

            using (var fbd = new VistaFolderBrowserDialog())
            {
                fbd.Description = "Select a artifacts destination folder.";
                fbd.SelectedPath = Path.GetDirectoryName(srcPaths[0]) + Path.DirectorySeparatorChar;
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;
                destPath = fbd.SelectedPath;
            }

            UnpackSongs(srcPaths, destPath);
        }

        private void cmbAppIds_SelectedValueChanged(object sender, EventArgs e)
        {
            if (cmbAppId.SelectedItem != null)
                NewAppId = ((SongAppId)cmbAppId.SelectedItem).AppId;
        }

        private void cmbGameVersion_SelectedIndexChanged(object sender, EventArgs e)
        {
            Version = (GameVersion)Enum.Parse(typeof(GameVersion), cmbGameVersion.SelectedItem.ToString());
            PopulateAppIdCombo(Version);
        }

        private void txtAppId_Validating(object sender, CancelEventArgs e)
        {
            var appId = ((TextBox)sender).Text.Trim();
            SelectComboAppId(appId);
        }

        public static Label CurrentOperationLabel { get; set; }
        public static ProgressBar UpdateProgress { get; set; }


    }
}
