using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CASCEdit.Helpers;
using CASCEdit.Structs;
using System.Threading.Tasks;
using System.Diagnostics;
using CASCEdit.IO;
using System.Net;

namespace CASCEdit.Handlers
{
	public class RootHandler : IDisposable
	{
		public RootChunk GlobalRoot { get; private set; }
		public SortedDictionary<string, CASFile> NewFiles { get; private set; } = new SortedDictionary<string, CASFile>();
		public List<RootChunk> Chunks { get; private set; } = new List<RootChunk>();

		private LocaleFlags locale;
		private uint maxId = 0;
		private readonly uint minimumId;
		private readonly EncodingMap encodingMap;

        private int namedFiles = 0;
        private int allFiles = 0;
        private int parsedFiles = 0;
        private const int headerMagic = 0x4D465354; // MFST 


        private Dictionary<uint, ulong> ListFile = new Dictionary<uint, ulong>();
        private WebClient ListFileClient = new WebClient();
  

        public RootHandler()
		{
			GlobalRoot = new RootChunk() { contentFlags = ContentFlags.None, localeFlags = LocaleFlags.All_WoW };
			encodingMap = new EncodingMap(EncodingType.ZLib, 9);
		}

		public RootHandler(Stream data, LocaleFlags locale, uint minimumid = 0, bool onlineListfile = false)
		{
			this.minimumId = minimumid;
			this.locale = locale;
            string cdnPath = Helper.GetCDNPath("listfile.csv");

            if (!File.Exists( cdnPath) && onlineListfile)
            {
                CASContainer.Logger.LogInformation("Downloading listfile from WoW.Tools");
                ListFileClient.DownloadFile("https://wow.tools/casc/listfile/download/csv/unverified", cdnPath);
            }

            BinaryReader stream = new BinaryReader(data);

            // 8.2 root change
            int magic = stream.ReadInt32();
            bool newFormat = magic == headerMagic;
            if (newFormat)
            {
                allFiles = stream.ReadInt32();
                namedFiles = stream.ReadInt32();
            }
            else
            {
                stream.BaseStream.Position = 0;
            }

			Dictionary<uint, string> conDict = new Dictionary<uint, string> { };

			long length = stream.BaseStream.Length;
			while (stream.BaseStream.Position < length)
			{
				uint cnt = stream.ReadUInt32();
				uint cflag = stream.ReadUInt32();
				uint loc = stream.ReadUInt32();

				//generate binary array and do a bit compare
				// Fabian Today at 10:30
				//0, 1, 2, 4, 8, 16, 32, 64, 128, 256, ...
				//are the single flags
				//any other number that doesnt fit in this sequence is just a combination of them
				for (uint i = 0; i < 32; i++)
				{
					uint j =(uint)Math.Pow(2, i);
					if ((j & cflag) > 0 )
					{
						if (!conDict.ContainsKey(j))
						{
							conDict.Add(j, "0x" + j.ToString("X4"));
						}
					}
				}

				RootChunk chunk = new RootChunk()
				{
					Count = cnt,
					contentFlags = (ContentFlags)cflag,
					localeFlags = (LocaleFlags)loc,		
				};

                parsedFiles += (int)chunk.Count;

                // set the global root
                if (chunk.localeFlags == LocaleFlags.All_WoW && chunk.contentFlags == ContentFlags.None)
					GlobalRoot = chunk;

				// trouble for shadowland prepatch 9.0
				//discord shearx#2824 
				//in fact, the chunk.ContentFlags is never equal to ContentFlags.None since the prepatch it seems
				//welp i got it working, but not in a way that I think will work for long lol
				//in an older version that worked, chunk.Count was equal to 19 on the GlobalRoot
				//so i just had it check for a chunk who's Count property was also equal to 19
				//and that got it started...
				//if (chunk.Count == 19)
				//	GlobalRoot = chunk;
				//yea as expected things are definitely not working right
				//best I stop fiddling around and just wait for proper updates lol


				//i tried looking only at chunks that used the locale All_WoW but that never worked
				//[00:23]
				//i tried using the lowest contentFlag for those with count == 19, that also didn't work
				//[00:23]
				//i tried using only the first chunk with count == 19, also also didn't work
				//[00:24]
				//ultimately just letting it overwrite the global root var until it ran out of chunks to process is what did it
				//[00:24]
				//which is why i say its not the right way lol because it very obviously isn't
				//[00:25]
				//but as I said earlier there are no chunks that meet the criteria of having their content flag == 0x0(None)
				//[00:25]
				//so I don't know what to even look for to make it be correct
				//[00:26]
				//i don't know where to find a list of the flags either to see if there was actually a documented change

				uint fileDataIndex = 0;
				for (int i = 0; i < chunk.Count; i++)
				{
					uint offset = stream.ReadUInt32();

					RootEntry entry = new RootEntry()
					{
						FileDataIdOffset = offset,
						FileDataId = fileDataIndex + offset
					};

					fileDataIndex = entry.FileDataId + 1;
					chunk.Entries.Add(entry);
				}

                if (newFormat)
                {
                    foreach (var entry in chunk.Entries)
                    {
                        entry.CEKey = new MD5Hash(stream);
                        maxId = Math.Max(maxId, entry.FileDataId);
                    }

                    if (parsedFiles > allFiles - namedFiles)
                    {
                        foreach (var entry in chunk.Entries)
                        {
                            entry.NameHash = stream.ReadUInt64();
                        }
                    }
                    else // no namehash
                    {
                        foreach (var entry in chunk.Entries)
                        {
                            entry.NameHash = 0;
                        }
                    }

                }
                else
                {
                    foreach (var entry in chunk.Entries)
                    {
                        entry.CEKey = new MD5Hash(stream);
                        entry.NameHash = stream.ReadUInt64();
                        maxId = Math.Max(maxId, entry.FileDataId);
                    }
                }

                Chunks.Add(chunk);
			}

			//output new content flag
			var sortAscendingByKey = from pair in conDict orderby pair.Key ascending select pair; //

			// File name  
			string fileName = @"contentflags.txt";
			FileStream streamwriter = null;
			try
			{
				// Create a FileStream with mode CreateNew  
				streamwriter = new FileStream(fileName, FileMode.Create);
				// Create a StreamWriter from FileStream  
				using (StreamWriter writer = new StreamWriter(streamwriter, Encoding.UTF8))
				{
					writer.WriteLine("unit, hex");
					foreach ( var subdict in sortAscendingByKey)
					{
						writer.WriteLine(String.Format("{0}, {1}", subdict.Key ,subdict.Value ));
					}
					
				}
			}
			finally
			{
				if (streamwriter != null)
					streamwriter.Dispose();
			}

			if (GlobalRoot == null)
			{
				CASContainer.Logger.LogCritical($"No Global root found. Root file is corrupt.");
				return;
			}

            // use listfile to assign names
            var listFileLines = File.ReadAllLines(cdnPath);
            foreach (var listFileData in listFileLines)
            {
                var splitData = listFileData.Split(';');

                if (splitData.Length != 2)
                    continue;

                if (!uint.TryParse(splitData[0], out uint listFileDataID))
                    continue;

                ListFile[listFileDataID] = new Jenkins96().ComputeHash(splitData[1]);
            }

            foreach (var chunk in Chunks)
            {
                foreach (var entry in chunk.Entries)
                {
                    if (entry.NameHash == 0)
                    {
                        if (ListFile.ContainsKey(entry.FileDataId))
                            entry.NameHash = ListFile[entry.FileDataId];
                    }
                }
            }

            // set maxid from cache
            maxId = Math.Max(Math.Max(maxId, minimumid), CASContainer.Settings.Cache?.MaxId ?? 0);

			// store encoding map
			encodingMap = (data as BLTEStream)?.EncodingMap.FirstOrDefault() ?? new EncodingMap(EncodingType.ZLib, 9);

			stream?.Dispose();
			data?.Dispose();
		}

		public void RemoveDeleted()
		{
			if (CASContainer.Settings?.Cache == null)
				return;

			var entries = GlobalRoot.Entries.Where(x => x.FileDataId >= minimumId).ToList(); // avoid collection change errors
			foreach (var e in entries)
			{
				if (!CASContainer.Settings.Cache.HasId(e.FileDataId))
				{
					GlobalRoot.Entries.Remove(e);
					File.Delete(Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(e.CEKey.ToString(), "data")));
				}
			}

		}

		public void AddEntry(string path, CASResult file)
		{
			var cache = CASContainer.Settings.Cache;

			ulong namehash = new Jenkins96().ComputeHash(path);

			var entries = Chunks
						.FindAll(chunk => chunk.localeFlags.HasFlag(locale)) // Select locales that match selected locale
						.SelectMany(chunk => chunk.Entries) // Flatten the array to get all entries within all matching chunks
						.Where(e => e.NameHash == namehash);
						
			if (entries.Count() == 0)
            { // New file, we need to create an entry for it
				var cached = cache.Entries.FirstOrDefault(x => x.Path == path);
				var fileDataId = Math.Max(maxId + 1, minimumId);

				if (cached != null) {
					fileDataId = cached.FileDataId;
				}

				var entry = new RootEntry() {
					CEKey = file.CEKey,
					FileDataId = fileDataId,
					FileDataIdOffset = 0,
					NameHash = namehash,
					Path = path
				};

				GlobalRoot.Entries.Add(entry); // Insert into the Global Root
				maxId = Math.Max(entry.FileDataId, maxId); // Update the max id

                cache?.AddOrUpdate(new CacheEntry(entry, file.EKey)); // If not done, sometimes files will not be added.
            }
            else
            { // Existing file, we just have to update the data hash
				foreach (var entry in entries)
                {
					entry.CEKey = file.CEKey;
					entry.Path = path;

					cache?.AddOrUpdate(new CacheEntry(entry, file.EKey));
				}
			}
		}

		private void FixOffsets()
		{
			foreach (var root in Chunks)
			{
				root.Entries.Sort((x, y) => x.FileDataId.CompareTo(y.FileDataId));

				for (int i = 1; i < root.Entries.Count; i++)
				{
					var prevId = root.Entries[i - 1].FileDataId;
					var current = root.Entries[i];

					if (prevId + current.FileDataIdOffset + 1 != current.FileDataId)
						current.FileDataIdOffset = current.FileDataId - prevId - 1;
				}
			}
		}

		public CASResult Write()
		{
			FixOffsets();

			using (var md5 = MD5.Create())
			using (MemoryStream ms = new MemoryStream())
			using (BinaryWriter bw = new BinaryWriter(ms))
			{
				// write each chunk
				foreach (var c in Chunks)
				{
					bw.Write((uint)c.Entries.Count);
					bw.Write((uint)c.contentFlags);
					bw.Write((uint)c.localeFlags);

					foreach (var e in c.Entries)
						bw.Write(e.FileDataIdOffset);

					foreach (var e in c.Entries)
					{
						bw.Write(e.CEKey.Value);
						bw.Write(e.NameHash);
					}
				}

				// create CASCFile
				CASFile entry = new CASFile(ms.ToArray(), encodingMap.Type, encodingMap.CompressionLevel);

				// save and update Build Config
				CASResult res = DataHandler.Write(WriteMode.CDN, entry);
				res.CEKey = new MD5Hash(md5.ComputeHash(ms.ToArray()));
				res.HighPriority = true;
				CASContainer.BuildConfig.Set("root", res.CEKey.ToString());

				CASContainer.Logger.LogInformation($"Root: EKey: {res.EKey} CEKey: {res.CEKey}");

				// cache Root Hash
				CASContainer.Settings.Cache?.AddOrUpdate(new CacheEntry() { CEKey = res.CEKey, EKey = res.EKey, Path = "__ROOT__" });

				return res;
			}
		}


		#region File Methods

		public BLTEStream OpenFile(string cascpath)
		{
			var entry = GetEntry(cascpath);
			if (entry != null && CASContainer.EncodingHandler.CEKeys.TryGetValue(entry.CEKey, out EncodingCEKeyPageTable enc))
			{
				LocalIndexEntry idxInfo = CASContainer.LocalIndexHandler.GetIndexInfo(enc.EKeys[0]);
				if (idxInfo != null)
				{
					var path = Path.Combine(CASContainer.BasePath, "Data", "data", string.Format("data.{0:D3}", idxInfo.Archive));
					return DataHandler.Read(path, idxInfo);
				}
				else
				{
					return DataHandler.ReadDirect(Path.Combine(CASContainer.Settings.OutputPath, Helper.GetCDNPath(enc.EKeys[0].ToString(), "data")));
				}
			}

			return null;
		}

		public void AddFile(string filepath, string cascpath, EncodingType encoding = EncodingType.ZLib, byte compression = 9)
		{
			if (File.Exists(filepath))
				NewFiles.Add(cascpath, new CASFile(File.ReadAllBytes(filepath), encoding, compression));
		}

		public void RenameFile(string path, string newpath)
		{
			ulong hash = new Jenkins96().ComputeHash(path);
			ulong newhash = new Jenkins96().ComputeHash(newpath);

			foreach (var root in Chunks)
			{
				if (!root.localeFlags.HasFlag(locale) && root != GlobalRoot) // ignore incorrect locale and not global
					continue;

				var entries = root.Entries.Where(x => x.NameHash == hash);
				foreach (var entry in entries)
				{
					var blte = CASContainer.EncodingHandler.CEKeys[entry.CEKey].EKeys[0];
					entry.NameHash = newhash;
					entry.Path = path;

					CASContainer.Settings.Cache?.AddOrUpdate(new CacheEntry(entry, blte));
				}
			}
		}

		public void RemoveFile(string path)
		{
			ulong hash = new Jenkins96().ComputeHash(path);

			foreach (var root in Chunks)
			{
				var entries = root.Entries.Where(x => x.NameHash == hash).ToArray(); // should only ever be one but just incase
				foreach(var entry in entries)
				{
					if (CASContainer.EncodingHandler.CEKeys.TryGetValue(entry.CEKey, out EncodingCEKeyPageTable enc))
					{
						CASContainer.DownloadHandler?.RemoveEntry(enc.EKeys[0]); // remove from download
						CASContainer.CDNIndexHandler?.RemoveEntry(enc.EKeys[0]); // remove from cdn index
					}

					root.Entries.Remove(entry);
					CASContainer.Settings.Cache?.Remove(path);
				}
			}
		}

		#endregion


		#region Entry Methods

		public RootEntry GetEntry(string cascpath) => GetEntry(new Jenkins96().ComputeHash(cascpath));

		public RootEntry GetEntry(uint fileid) => GlobalRoot.Entries.Where(x => x.FileDataId == fileid).OrderByDescending(x => x.FileDataId).FirstOrDefault();

		public RootEntry GetEntry(ulong hash) => GlobalRoot.Entries.Where(x => x.NameHash == hash).OrderByDescending(x => x.FileDataId).FirstOrDefault();

		#endregion


		public void Dispose()
		{
			NewFiles.Clear();
			NewFiles = null;
			Chunks.Clear();
			Chunks.TrimExcess();
			Chunks = null;
		}
	}
}
