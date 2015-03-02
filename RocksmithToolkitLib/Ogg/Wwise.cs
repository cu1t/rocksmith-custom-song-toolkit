﻿using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using RocksmithToolkitLib.Extensions;

namespace RocksmithToolkitLib.Ogg
{
    public static class Wwise
    {
        public static void Convert2Wem(string sourcePath, string destinationPath, int audioQuality)
        {
            try
            {
                LoadWwiseTemplate(sourcePath, audioQuality);
                ExternalApps.Wav2Wem(GetWwisePath());
                GetWwiseFiles(destinationPath);
            }
            catch (Exception ex)
            {
                throw new Exception("Wwise audio file conversion failed: " + ex.Message);
            }
        }

        public static string GetWwisePath()
        {
            string programsDir = String.Empty;
            try
            {
                if (Environment.OSVersion.Version.Major >= 6)
                    programsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Audiokinetic\\Wwise v2013.2.2 build 4828");
                else
                    programsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Audiokinetic\\Wwise v2013.2.2 build 4828");

                string[] pathWwiseCli = Directory.GetFiles(programsDir, "WwiseCLI.exe", SearchOption.AllDirectories);

                if (String.IsNullOrEmpty(Path.GetFileName(pathWwiseCli[0])))
                    throw new FileNotFoundException("Could not find WwiseCLI.exe");

                return pathWwiseCli[0];
            }
            catch (Exception ex)
            {
                MessageBox.Show(new Form { TopMost = true }, @"Could not find WwiseCLI.exe or Audiokinetic directory  ", @"Exception: " + ex.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);

                Application.Exit();
                Environment.Exit(-1);
                return null;
            }
        }

        public static void LoadWwiseTemplate(string sourcePath, int audioQuality)
        {
            var appRootDir = Path.GetDirectoryName(Application.ExecutablePath);
            var templateDir = Path.Combine(appRootDir, "Wwise\\Template");
            var orgSfxDir = Path.Combine(appRootDir, templateDir, "Originals\\SFX");

            if (!Directory.Exists(orgSfxDir))
                throw new FileNotFoundException("Could not find Wwise template originals SFX directory.\r\nReinstall Midi2RsXml to fix problem.");

            if (File.Exists(Path.Combine(templateDir, "Template.Administrator.validationcache")))
                File.Delete(Path.Combine(templateDir, "Template.Administrator.validationcache"));

            if (Directory.Exists(Path.Combine(templateDir, ".cache")))
                Directory.Delete(Path.Combine(templateDir, ".cache"), true);

            // cleanup gives new hex value to WEM files
            if (Directory.Exists(Path.Combine(templateDir, "GeneratedSoundBanks")))
                Directory.Delete(Path.Combine(templateDir, "GeneratedSoundBanks"), true);

            var dirName = Path.GetDirectoryName(sourcePath);
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var dirFileName = Path.Combine(dirName, fileName);
            var sourcePreviewWave = String.Format("{0}_{1}.wav", dirFileName, "preview");

            File.Copy(sourcePath, Path.Combine(orgSfxDir, "Audio.wav"), true);
            File.Copy(sourcePreviewWave, Path.Combine(orgSfxDir, "Audio_preview.wav"), true);

 
            string resString = String.Empty;
            var resName = "RocksmithToolkitLib.Resources.QF Default Work Unit.wwu";
            Assembly assem = Assembly.GetExecutingAssembly();
            string[] names = assem.GetManifestResourceNames();
            var stream = assem.GetManifestResourceStream(resName);

            if (stream != null)
            {
                var reader = new StreamReader(stream);
                resString = reader.ReadToEnd();
            }
            else
                throw new Exception("Can not find Audio Quality Factor resource");

            var workUnitPath = Path.Combine(templateDir, "Interactive Music Hierarchy", "Default Work Unit.wwu");
            resString = resString.Replace("%QF1%", Convert.ToString(audioQuality));
            resString = resString.Replace("%QF2%", "4");
            using (TextWriter tw = new StreamWriter(workUnitPath, false))
            {
                tw.Write(resString);
                tw.Close();
            }
        }


        public static void GetWwiseFiles(string destinationPath)
        {
            var appRootDir = Path.GetDirectoryName(Application.ExecutablePath);
            var wemDir = @"Wwise\Template\.cache\Windows\SFX";
            var wemPath = Path.Combine(appRootDir, wemDir);

            if (!Directory.Exists(wemPath))
                throw new FileNotFoundException("Could not find Wwise template .cache Windows SFX directory");

            var destPreviewPath = Path.Combine(Path.GetDirectoryName(destinationPath), Path.GetFileName(destinationPath).Substring(0, Path.GetFileName(destinationPath).Length - 4) + "_preview.wem");

            string[] srcPaths = Directory.GetFiles(wemPath, "*.wem", SearchOption.TopDirectoryOnly);
            if (srcPaths.Length != 2)
                throw new Exception("Did not find converted Wem audio and preview files");

            foreach (string srcPath in srcPaths)
            {
                if (srcPath.Contains("_preview_"))
                    File.Copy(srcPath, destPreviewPath, true);
                else
                    File.Copy(srcPath, destinationPath, true);
            }
        }


    }
}