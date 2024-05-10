using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using UnrealUnZen;
using File = System.IO.File;

namespace UEcastocLib
{
    public static class Packer
    {
        public class FileProcessedEventArguments
        {
            public string CurrentFilePath;
            public int CurrentFileNumber;
            public int TotalFilesNumber;
            public ulong FilesUnpackedSize;
            public ulong AllFilesSize;
        }

        public delegate void FilePackedDelegate(FileProcessedEventArguments fileProcessedEventArguments);
        public delegate void FinishedPackingDelegate(int filesPacked);

        public static event FilePackedDelegate FilePacked;
        public static event FinishedPackingDelegate FinishedPacking;

        public delegate void ManifestFileProcessedDelegate(FileProcessedEventArguments fileProcessedEventArguments);

        public static event ManifestFileProcessedDelegate ManifestFileProcessed;

        public delegate void PakWrittenDelegate(long bytesWrittenTotal, long pakBytesTotal);

        public static event PakWrittenDelegate PakWritten;


        const int CompSize = 0x10000;
        public static int PackUtocVersion = 3;
        const int CompressionNameLength = 32;

        public static Manifest JsonToManifest(string manifestPath)
        {

            string json = File.ReadAllText(manifestPath);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<Manifest>(json);

        }

        public static void PackGameFiles(string UTocFilePath, UTocData UTocFile, string repackFolderPath, string outFile,
            string compressionMethod, string AESKey)
        {
            Manifest manifest = UTocFile.ConstructManifest(Path.ChangeExtension(UTocFilePath, ".ucas"));
            FixManifest(manifest, repackFolderPath);
            PackGameFiles(repackFolderPath, manifest, outFile, compressionMethod, AESKey);
        }

        private static void FixManifest(Manifest manifest, string repackFolderPath)
        {
            var TempManifestFiles = manifest.Files.ToList();
            foreach (var (manifestFile, currentIndex) in TempManifestFiles.Select((manifestFile, currentIndex) => (manifestFile, currentIndex)))
            {
                bool fileExists = File.Exists(Path.Combine(repackFolderPath, manifestFile.Filepath.Replace("/", "\\")));
                bool isADependency = manifestFile.Filepath == "dependencies";

                if (fileExists || isADependency) continue;

                manifest.Files.Remove(manifestFile);
                manifest.Deps.ChunkIDToDependencies.Remove(ulong.Parse(manifestFile.ChunkID.Substring(0, 16), System.Globalization.NumberStyles.HexNumber));

                ManifestFileProcessed?.Invoke(new FileProcessedEventArguments
                {
                    CurrentFilePath = manifestFile.Filepath,
                    CurrentFileNumber = currentIndex,
                    TotalFilesNumber = TempManifestFiles.Count
                });
            }
        }
        public static int PackGameFiles(string repackFolderPath, Manifest manifest, string outFile, string compressionMethod, string AESKey)
        {
            repackFolderPath = Path.GetFullPath(repackFolderPath);
            outFile = Path.ChangeExtension(outFile, null); // Remove any extension

            string compression = "None";
            if (!string.IsNullOrEmpty(compressionMethod))
            {
                compression = compressionMethod;
            }

            byte[] aes = Helpers.HexStringToByteArray(AESKey);
            if (aes.Length > 0 && aes.Length != 32)
            {
                throw new Exception("AES key length should be 32 bytes or none at all");
            }

            // TODO Catch the manifest in the calling code to gracefully handle the exception
            //Manifest manifest = ReadManifest(manifestPath);
            if (manifest == null)
            {
                throw new Exception("Manifest read is null");
            }

            int numberOfFilesPacked = PackToCasToc(repackFolderPath, manifest, outFile, compression, aes);

            WriteEmbeddedPakFile(outFile);

            FinishedPacking?.Invoke(numberOfFilesPacked);

            return numberOfFilesPacked;
        }

        private static void WriteEmbeddedPakFile(string outFileName)
        {
            const int Megabyte = 1048576;

            string outFileFullName = outFileName + ".pak";
            byte[] embedded = ToolResources.Packed_P;
            MemoryStream packedEmbeddedPakStream = new MemoryStream(embedded);

            using (FileStream fileStream = File.Open(outFileFullName, FileMode.OpenOrCreate))
            {
                while (true)
                {
                    byte[] readingBytesBuffer = new byte[Megabyte];
                    int totalNumberOfBytesRead = packedEmbeddedPakStream.Read(readingBytesBuffer, 0, readingBytesBuffer.Length);

                    if (totalNumberOfBytesRead == 0) break;

                    fileStream.Write(readingBytesBuffer, 0, totalNumberOfBytesRead);

                    PakWritten?.Invoke(packedEmbeddedPakStream.Position + 1, packedEmbeddedPakStream.Length);
                }
            }
        }

        public static List<GameFileMetaData> ListFilesInDir(string dir, Dictionary<string, FIoChunkID> pathToChunkID)
        {
            var files = new List<GameFileMetaData>();
            Directory.GetFiles(dir, "*", SearchOption.AllDirectories).ToList().ForEach(path =>
            {
                var info = new FileInfo(path);
                var mountedPath = Path.GetFullPath(path).Replace("\\", "/").Substring(dir.Length).TrimStart('/');
                var offlen = new FIoOffsetAndLength();
                offlen.SetLength((ulong)info.Length);

                if (!pathToChunkID.TryGetValue(mountedPath, out var chidData))
                {
                    throw new Exception("A problem occurred while constructing the file. Did you use the correct manifest file?");
                }

                var newEntry = new GameFileMetaData
                {
                    FilePath = mountedPath,
                    ChunkID = chidData,
                    OffLen = offlen
                };

                files.Add(newEntry);
            });

            return files;
        }

        public static void PackFilesToUcas(this List<GameFileMetaData> files, Manifest m, string dir, string outFilename, string compression)
        {
            /* manually add the "dependencies" section here */
            // only include the dependencies that are present
            var subsetDependencies = new Dictionary<ulong, FileDependency>();
            if (m.Deps.ChunkIDToDependencies.Count > 0)
            {
                foreach (var file in files)
                {
                    if (m.Deps.ChunkIDToDependencies.ContainsKey(file.ChunkID.ID) && !subsetDependencies.ContainsKey(file.ChunkID.ID))
                        subsetDependencies.Add(file.ChunkID.ID, m.Deps.ChunkIDToDependencies[file.ChunkID.ID]);
                }
            }

            m.Deps.ChunkIDToDependencies = subsetDependencies;

            var depHexString = m.Files.FirstOrDefault(v => v.Filepath == Constants.DepFileName).ChunkID;
            var compMethodNumber = compression.ToLower() != "none" ? (byte)1 : (byte)0;
            var compFun = CompressionUtils.GetCompressionFunction(compression);

            if (compFun == null)
            {
                throw new Exception("Could not find " + compression + " method. Please use none, oodle or zlib");
            }

            var allFilesSize = UCasDataParser.GetAllFilesSize(files);
            ulong filesPackedSize = 0;
            Directory.CreateDirectory(Path.GetDirectoryName(outFilename));
            using (var ucasFile = File.Create(outFilename + ".ucas"))
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var currentFileMetaData = files[i];
                    MemoryMappedFile MemoryMappedFile;
                    long SizeOfmmf;
                    string pathToread = Path.Combine(dir.Replace("/", "\\"), currentFileMetaData.FilePath.Replace("/", "\\"));
                    if (!File.Exists(pathToread))
                    {
                        if (currentFileMetaData.FilePath != Constants.DepFileName) throw new Exception("File doesn't exist, and also its not the dependency file.");
                        byte[] ManifestCreatedFile = m.Deps.DeparseDependencies();
                        MemoryMappedFile = Helpers.CreateMemoryMappedFileFromByteArray(ManifestCreatedFile, currentFileMetaData.FilePath);
                        SizeOfmmf = ManifestCreatedFile.LongLength;
                        currentFileMetaData.FilePath = "";
                        currentFileMetaData.ChunkID = FIoChunkID.FromHexString(depHexString);
                        currentFileMetaData.ChunkID.ID = Helpers.RandomUlong();
                    }
                    else
                    {
                        MemoryMappedFile = MemoryMappedFile.CreateFromFile(pathToread, FileMode.Open);
                        SizeOfmmf = new FileInfo(pathToread).Length;
                    }


                    currentFileMetaData.OffLen.SetLength((ulong)SizeOfmmf);

                    if (i == 0)
                    {
                        currentFileMetaData.OffLen.SetOffset(0);
                    }
                    else
                    {
                        var off = files[i - 1].OffLen.GetOffset() + files[i - 1].OffLen.GetLength();
                        off = ((off + CompSize - 1) / CompSize) * CompSize;
                        currentFileMetaData.OffLen.SetOffset(off);
                    }

                    currentFileMetaData.Metadata.ChunkHash = new FIoChunkHash(Helpers.SHA1Hash(MemoryMappedFile));
                    currentFileMetaData.Metadata.Flags = FIoStoreTocEntryMetaFlags.CompressedMetaFlag;

                    long PosOfReaded = 0;
                    long RemainSize = SizeOfmmf;
                    while (PosOfReaded != SizeOfmmf)
                    {
                        var block = new FIoStoreTocCompressedBlockEntry();
                        var chunkLen = RemainSize;
                        if (chunkLen > CompSize)
                        {
                            chunkLen = CompSize;
                        }
                        RemainSize -= chunkLen;
                        var chunk = MemoryMappedFile.ReadBytesOfFile(PosOfReaded, chunkLen);
                        PosOfReaded += chunkLen;
                        var cChunkPtr = compFun(chunk);
                        var compressedChunk = cChunkPtr.ToArray();

                        block.CompressionMethod = compMethodNumber;
                        block.SetOffset((ulong)ucasFile.Position);
                        block.SetUncompressedSize((uint)chunkLen);
                        block.SetCompressedSize((uint)compressedChunk.Length);

                        compressedChunk = compressedChunk.Concat(Helpers.GetRandomBytes((0x10 - (compressedChunk.Length % 0x10)) & (0x10 - 1))).ToArray();
                        currentFileMetaData.CompressionBlocks.Add(block);

                        ucasFile.Write(compressedChunk, 0, compressedChunk.Length);
                    }

                    filesPackedSize += currentFileMetaData.OffLen.GetLength();

                    FilePacked?.Invoke(new FileProcessedEventArguments {
                        CurrentFilePath = currentFileMetaData.FilePath,
                        CurrentFileNumber = i,
                        TotalFilesNumber = files.Count,
                        FilesUnpackedSize = filesPackedSize,
                        AllFilesSize = allFilesSize
                    });
                }
            }
        }

        public static byte[] ToBytes(this DirIndexWrapper w)
        {
            MemoryStream output = new MemoryStream();

            uint dirCount = (uint)w.Dirs.Count;
            uint fileCount = (uint)w.Files.Count;
            uint strCount = (uint)w.StrSlice.Count();

            // Mount point string
            byte[] mountPointStr = Helpers.StringToFString(Constants.MountPoint);
            output.Write(mountPointStr);

            // Directory index entries
            output.Write(dirCount);
            foreach (FIoDirectoryIndexEntry directoryEntry in w.Dirs)
            {
                directoryEntry.Write(output);
            }

            // File index entries
            output.Write(fileCount);
            foreach (FIoFileIndexEntry fileEntry in w.Files)
            {
                fileEntry.Write(output);
            }

            // String table
            output.Write(strCount);
            foreach (string str in w.StrSlice)
            {
                byte[] strBytes = Helpers.StringToFString(str);
                output.Write(strBytes);
            }

            return output.ToArray();
        }

        public static byte[] DeparseDirectoryIndex(List<GameFileMetaData> files)
        {
            var wrapper = new DirIndexWrapper();
            var dirIndexEntries = new List<FIoDirectoryIndexEntry>();
            var fileIndexEntries = new List<FIoFileIndexEntry>();

            // first, create unique slice of strings
            var strmap = new Dictionary<string, bool>();
            foreach (var v in files)
            {
                var dirfiles = v.FilePath.Split('/');
                if (dirfiles[0] == "")
                {
                    dirfiles = dirfiles.Skip(1).ToArray();
                }

                foreach (var str in dirfiles)
                {
                    strmap[str] = true;
                }
            }

            var strSlice = strmap.Keys.ToList();
            // of this, create a map for quick lookup
            var strIdx = new Dictionary<string, int>();
            for (int iv = 0; iv < strSlice.Count; iv++)
            {
                strIdx.Add(strSlice[iv], iv);
            }


            var root = new FIoDirectoryIndexEntry
            {
                Name = Constants.NoneEntry,
                FirstChildEntry = Constants.NoneEntry,
                NextSiblingEntry = Constants.NoneEntry,
                FirstFileEntry = Constants.NoneEntry,
            };

            dirIndexEntries.Add(root);
            wrapper.Dirs = dirIndexEntries;
            wrapper.Files = fileIndexEntries;
            wrapper.StrTable = strIdx;
            wrapper.StrSlice = strSlice.ToArray();

            for (var i = 0; i < files.Count; i++)
            {
                var fpathSections = files[i].FilePath.Split('/');
                if (fpathSections[0] == "")
                {
                    fpathSections = fpathSections.Skip(1).ToArray();
                }

                root.AddFile(fpathSections, (uint)i, wrapper);
            }

            return wrapper.ToBytes();
        }

        public static byte[] ConstructUtocFile(this List<GameFileMetaData> files, string compression, byte[] AESKey)
        {
            var udata = new UTocData
            {
                Header = new UTocHeader(),
                Files = new List<GameFileMetaData>(),
                MountPoint = "",
                CompressionMethods = new List<string>()
            };

            var newContainerFlags = (byte)EIoContainerFlags.IndexedContainerFlag;
            var compressionMethods = new List<string> { "None" };

            if (compression.ToLower() != "none")
            {
                compressionMethods.Add(compression);
                newContainerFlags |= (byte)EIoContainerFlags.CompressedContainerFlag;
            }

            if (AESKey.Length != 0)
            {
                newContainerFlags |= (byte)EIoContainerFlags.EncryptedContainerFlag;
            }

            var compressedBlocksCount = (int)0;
            var containerIndex = 0;

            for (var i = 0; i < files.Count; i++)
            {
                compressedBlocksCount += files[i].CompressionBlocks.Count;
                if (files[i].ChunkID.Type == 10)
                {
                    containerIndex = i;
                }
            }

            var dirIndexBytes = DeparseDirectoryIndex(files);

            udata.Header = new UTocHeader
            {
                Magic = Constants.MagicUtoc,
                Version = (byte)PackUtocVersion,
                HeaderSize = (uint)udata.Header.SizeOf(),
                EntryCount = (uint)files.Count,
                CompressedBlockEntryCount = (uint)compressedBlocksCount,
                CompressedBlockEntrySize = 12,
                CompressionMethodNameCount = (uint)(compressionMethods.Count - 1),
                CompressionMethodNameLength = CompressionNameLength,
                CompressionBlockSize = CompSize,
                DirectoryIndexSize = (uint)dirIndexBytes.Length,
                ContainerID = new FIoContainerID(files[containerIndex].ChunkID.ID),
                ContainerFlags = (EIoContainerFlags)newContainerFlags,
                PartitionSize = ulong.MaxValue,
                PartitionCount = 1
            };

            using (var buf = new MemoryStream())
            {

                // write header
                udata.Header.Write(buf);

                // write chunk IDs
                foreach (var file in files)
                {
                    file.ChunkID.Write(buf);
                }

                // write Offset and lengths
                foreach (var file in files)
                {
                    file.OffLen.Write(buf);
                }

                // write compression blocks
                foreach (var file in files)
                {
                    foreach (var block in file.CompressionBlocks)
                    {
                        block.Write(buf);
                    }
                }

                // write compression methods, but skip "none"
                foreach (var compMethod in compressionMethods)
                {
                    if (compMethod.ToLower() == "none")
                    {
                        continue;
                    }

                    var capitalized = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(compMethod);
                    var bname = Encoding.ASCII.GetBytes(capitalized);
                    var paddedName = new byte[CompressionNameLength];
                    Array.Copy(bname, paddedName, bname.Length);
                    buf.Write(paddedName, 0, paddedName.Length);
                }

                // write directory index
                buf.Write(dirIndexBytes, 0, dirIndexBytes.Length);

                // write chunk metas
                foreach (var file in files)
                {
                    file.Metadata.Write(buf);
                }

                return buf.ToArray();
            }
        }

        public static int PackToCasToc(string directory, Manifest manifest, string outFilename, string compression, byte[] aes)
        {
            var gameFileMetaDatas = new List<GameFileMetaData>();
            GameFileMetaData newEntry;

            foreach (var file in manifest.Files)
            {
                var offlen = new FIoOffsetAndLength();
                var p = Path.Combine(directory, file.Filepath);
                if (File.Exists(p))
                {
                    offlen.SetLength((ulong)new FileInfo(p).Length);
                }
                else if (file.Filepath == Constants.DepFileName)
                {
                    offlen.SetLength(0); // Will be fixed in a later function
                }

                newEntry = new GameFileMetaData
                {
                    FilePath = file.Filepath,
                    ChunkID = FIoChunkID.FromHexString(file.ChunkID),
                    OffLen = offlen,
                    Metadata = new FIoStoreTocEntryMeta(),
                    CompressionBlocks = new List<FIoStoreTocCompressedBlockEntry>()
                };

                gameFileMetaDatas.Add(newEntry);
            }
            //var files = ListFilesInDir(dir, m.Files.ToDictionary(k => k.Filepath, v => FIoChunkID.FromHexString(v.ChunkID)));

            gameFileMetaDatas.PackFilesToUcas(manifest, directory, outFilename, compression);

            if (aes.Length != 0)
                Helpers.EncryptFileWithAES(outFilename + ".ucas", aes);

            byte[] utocBytes = gameFileMetaDatas.ConstructUtocFile(compression, aes);
            File.WriteAllBytes(outFilename + ".utoc", utocBytes);

            int gameFilesPackedTotal = gameFileMetaDatas.Count;
            return gameFilesPackedTotal;
        }
    }
}
