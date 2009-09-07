using System;
using System.Collections.Generic;
using System.IO;

namespace wyUpdate.Common
{
    // File information and instuctions for updates
    public class UpdateDetails
    {
        public UpdateDetails()
        {
            RegistryModifications = new List<RegChange>();
            UpdateFiles = new List<UpdateFile>();
            ShortcutInfos = new List<ShortcutInfo>();
            FoldersToDelete = new List<string>();
            PreviousSMenuShortcuts = new List<string>();
            PreviousDesktopShortcuts = new List<string>();
        }

        #region Properties

        public string PostUpdateCommands { get; set; }

        public List<RegChange> RegistryModifications { get; set; }

        public List<UpdateFile> UpdateFiles { get; set; }

        public List<ShortcutInfo> ShortcutInfos { get; set; }

        public List<string> PreviousDesktopShortcuts { get; set; }

        public List<string> PreviousSMenuShortcuts { get; set; }

        public List<string> FoldersToDelete { get; set; }

        #endregion Properties

        // count # NGEN or execute files
        private int CountFileInfos()
        {
            int count = 0;
            foreach (UpdateFile file in UpdateFiles)
            {
                if (file.Execute || file.IsNETAssembly)
                    count++;
            }

            return count;
        }

        public static UpdateDetails Load(string fileName)
        {
            UpdateDetails updtDetails = new UpdateDetails();

            FileStream fs = null;

            try
            {
                fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            }
            catch (Exception ex)
            {
                if (fs != null)
                    fs.Close();

                throw new ArgumentException("The update details file failed to open.\n\nFull details:\n\n" + ex.Message);
            }

            // Read back the file identification data, if any
            if (!ReadFiles.IsHeaderValid(fs, "IUUDFV2"))
            {
                //free up the file so it can be deleted
                fs.Close();

                throw new ArgumentException("The update details file failed to open because it has an incorrect file identifier.");
            }

            UpdateFile tempUpdateFile = new UpdateFile();

            byte bType = (byte)fs.ReadByte();
            while (!ReadFiles.ReachedEndByte(fs, bType, 0xFF))
            {
                switch (bType)
                {
                    case 0x01:
                        updtDetails.PostUpdateCommands = ReadFiles.ReadDeprecatedString(fs);
                        break;
                    case 0x20://num reg changes
                        updtDetails.RegistryModifications = new List<RegChange>(ReadFiles.ReadInt(fs));
                        break;
                    case 0x21://num file infos
                        updtDetails.UpdateFiles = new List<UpdateFile>(ReadFiles.ReadInt(fs));
                        break;
                    case 0x8E:
                        updtDetails.RegistryModifications.Add(RegChange.ReadFromStream(fs));
                        break;
                    case 0x30:
                        updtDetails.PreviousDesktopShortcuts.Add(ReadFiles.ReadDeprecatedString(fs));
                        break;
                    case 0x31:
                        updtDetails.PreviousSMenuShortcuts.Add(ReadFiles.ReadDeprecatedString(fs));
                        break;
                    case 0x40:
                        tempUpdateFile.RelativePath = ReadFiles.ReadDeprecatedString(fs);
                        break;
                    case 0x41:
                        tempUpdateFile.Execute = ReadFiles.ReadBool(fs);
                        break;
                    case 0x42:
                        tempUpdateFile.ExBeforeUpdate = ReadFiles.ReadBool(fs);
                        break;
                    case 0x43:
                        tempUpdateFile.CommandLineArgs = ReadFiles.ReadDeprecatedString(fs);
                        break;
                    case 0x44:
                        tempUpdateFile.IsNETAssembly = ReadFiles.ReadBool(fs);
                        break;
                    case 0x45:
                        tempUpdateFile.WaitForExecution = ReadFiles.ReadBool(fs);
                        break;
                    case 0x46:
                        tempUpdateFile.DeleteFile = ReadFiles.ReadBool(fs);
                        break;
                    case 0x47:
                        tempUpdateFile.DeltaPatchRelativePath = ReadFiles.ReadDeprecatedString(fs);
                        break;
                    case 0x48:
                        tempUpdateFile.NewFileAdler32 = ReadFiles.ReadLong(fs);
                        break;
                    case 0x9B://end of file
                        updtDetails.UpdateFiles.Add(tempUpdateFile);
                        tempUpdateFile = new UpdateFile();
                        break;
                    case 0x8D:
                        updtDetails.ShortcutInfos.Add(ShortcutInfo.LoadFromStream(fs));
                        break;
                    case 0x60:
                        updtDetails.FoldersToDelete.Add(ReadFiles.ReadDeprecatedString(fs));
                        break;
                    default:
                        ReadFiles.SkipField(fs, bType);
                        break;
                }

                bType = (byte)fs.ReadByte();
            }

            fs.Close();

            return updtDetails;
        }

        public Stream Save()
        {
            MemoryStream ms = new MemoryStream();

            // Write any file-identification data you want to here
            WriteFiles.WriteHeader(ms, "IUUDFV2");

            //Write post-update commands
            if (!string.IsNullOrEmpty(PostUpdateCommands))
                WriteFiles.WriteDeprecatedString(ms, 0x01, PostUpdateCommands);

            //number of registry changes
            WriteFiles.WriteInt(ms, 0x20, RegistryModifications.Count);

            for (int i = 0; i < RegistryModifications.Count; i++)
            {
                RegistryModifications[i].WriteToStream(ms, true);
            }

            //Shortcut information
            foreach (ShortcutInfo si in ShortcutInfos)
                si.SaveToStream(ms, true);


            //Previous shortcuts that needs to be installed in order to install new shortcuts
            foreach (string shortcut in PreviousDesktopShortcuts)
                WriteFiles.WriteDeprecatedString(ms, 0x30, shortcut);

            foreach (string shortcut in PreviousSMenuShortcuts)
                WriteFiles.WriteDeprecatedString(ms, 0x31, shortcut);

            //number of file infos
            WriteFiles.WriteInt(ms, 0x21, CountFileInfos());

            // write file info for ngening .NET, execution of files, etc.
            foreach (UpdateFile file in UpdateFiles)
            {
                if (file.Execute || file.IsNETAssembly || file.DeleteFile || file.DeltaPatchRelativePath != null)
                {
                    ms.WriteByte(0x8B);//Beginning of the file information

                    //relative path to file
                    WriteFiles.WriteDeprecatedString(ms, 0x40, file.RelativePath);

                    //execution of files
                    if (file.Execute)
                    {
                        //execute?
                        WriteFiles.WriteBool(ms, 0x41, file.Execute);

                        //execute before update?
                        WriteFiles.WriteBool(ms, 0x42, file.ExBeforeUpdate);

                        WriteFiles.WriteBool(ms, 0x45, file.WaitForExecution);

                        //commandline arguments
                        if (!string.IsNullOrEmpty(file.CommandLineArgs))
                            WriteFiles.WriteDeprecatedString(ms, 0x43, file.CommandLineArgs);
                    }

                    //is it a .NET assembly?
                    WriteFiles.WriteBool(ms, 0x44, file.IsNETAssembly);


                    //Delta update particulars:

                    if (file.DeleteFile)
                        WriteFiles.WriteBool(ms, 0x46, true);
                    else if (file.DeltaPatchRelativePath != null)
                    {
                        WriteFiles.WriteDeprecatedString(ms, 0x47, file.DeltaPatchRelativePath);

                        if (file.NewFileAdler32 != 0)
                            WriteFiles.WriteLong(ms, 0x48, file.NewFileAdler32);
                    }

                    ms.WriteByte(0x9B);//End of the file information
                }
            }

            foreach (string folder in FoldersToDelete)
                WriteFiles.WriteDeprecatedString(ms, 0x60, folder);

            //end of file
            ms.WriteByte(0xFF);

            ms.Position = 0;
            return ms;
        }
    }
}
