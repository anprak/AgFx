// (c) Copyright Microsoft Corporation.
// This source is subject to the Apache License, Version 2.0
// Please see http://www.apache.org/licenses/LICENSE-2.0 for details.
// All other rights reserved.



using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace AgFx.IsoStore
{

    internal class HashedIsoStoreProvider : StoreProviderBase
    {
        public const int FlushThreshholdBytes = 100000;

        private const char FileNameSeparator = '»';

        private const string CacheDirectoryName = "«c";

        internal const string CacheDirectoryPrefix = CacheDirectoryName + "\\";

        internal const string CacheDirectorySearchPrefix = CacheDirectoryPrefix + "*";

        private static object LockObject = new object();

        private StorageFolder Folder
        {
            get
            {
                return ApplicationData.Current.LocalFolder;
            }
        }

        public override bool IsBuffered
        {
            get { return false; }
        }


        public override IEnumerable<CacheItemInfo> GetItems()
        {
            try
            {
                var cacheFolder = Folder.GetFolderAsync(CacheDirectoryName).AsTask().RunSynchronouslyEx();
                var files = cacheFolder.GetFilesAsync(CommonFileQuery.DefaultQuery).AsTask().RunSynchronouslyEx();
                var items = from f in files
                            select new FileItem(f.Path).Item;
                return items;
            }
            catch (Exception)
            {
                return Enumerable.Empty<CacheItemInfo>();
            }
        }

        public override void DeleteAll(string uniqueName)
        {

            lock (_cache)
            {
                if (_cache.ContainsKey(uniqueName))
                {
                    _cache.Remove(uniqueName);
                }
            }

            // find the directory.
            //
            var dir = FileItem.DirectoryHash(uniqueName);

            StorageFolder folder = null;
            try
            {
                folder = Folder.GetFolderAsync(dir).AsTask().RunSynchronouslyEx();
            }
            catch (Exception)
            {
            }
            if (folder != null)
            {
                PriorityQueue.AddStorageWorkItem(async () =>
                    {
                            var files = await folder.GetFilesAsync();
                            await Task.WhenAll(files.Select(f => f.DeleteAsync().AsTask()));
                    });
            }
        }

        Dictionary<string, CacheItemInfo> _cache = new Dictionary<string, CacheItemInfo>();

        public override IEnumerable<CacheItemInfo> GetItems(string uniqueName)
        {


            CacheItemInfo item;

            if (_cache.TryGetValue(uniqueName, out item))
            {
                return new CacheItemInfo[] { item };
            }

            // find the directory.
            //
            var dir = FileItem.DirectoryHash(uniqueName);

            try
            {
                var folder = Folder.GetFolderAsync(dir).AsTask().RunSynchronouslyEx();

                lock (LockObject)
                {
                    var files = folder.GetFilesAsync().AsTask().RunSynchronouslyEx();

                    List<CacheItemInfo> items = new List<CacheItemInfo>();

                    foreach (var f in files)
                    {
                        CacheItemInfo cii = FileItem.FromFileName(f.Name);

                        if (cii != null)
                        {
                            items.Add(cii);
                        }
                    }

                    var orderedItems = from i in items
                                       where i.UniqueName == uniqueName
                                       orderby i.ExpirationTime descending
                                       select i;

                    foreach (var i in orderedItems)
                    {
                        if (item == null)
                        {
                            item = i;
                            continue;
                        }

                        Delete(i);
                    }

                    if (item != null)
                    {
                        _cache[uniqueName] = item;
                        return new CacheItemInfo[] { item };
                    }
                }

            }
            catch (Exception)
            {
            }

            return new CacheItemInfo[0];
        }

        public override CacheItemInfo GetLastestExpiringItem(string uniqueName)
        {
            var items = GetItems(uniqueName);

            return items.FirstOrDefault();
        }


        public override void Flush(bool synchronous)
        {

        }

        public override void Delete(CacheItemInfo item)
        {

            CacheItemInfo cachedItem;

            lock (_cache)
            {
                if (_cache.TryGetValue(item.UniqueName, out cachedItem) && Object.Equals(item, cachedItem))
                {
                    _cache.Remove(item.UniqueName);
                }
            }

            var fi = new FileItem(item);

            var fileName = fi.FileName;

            PriorityQueue.AddStorageWorkItem(async () =>
                   {
                       try
                       {
                           await (await Folder.GetFileAsync(fileName)).DeleteAsync();
                       }
                       catch (Exception)
                       {
                       }
                   });
        }

        public override byte[] Read(CacheItemInfo item)
        {
            var fi = new FileItem(item);
            byte[] bytes = null;

            lock (LockObject)
            {
                StorageFile file = null;

                try
                {
                    file = Folder.GetFileAsync(fi.FileName).AsTask().RunSynchronouslyEx();
                }
                catch (Exception)
                {
                    return null;
                }

                using (Stream stream = Folder.OpenStreamForReadAsync(fi.FileName).RunSynchronouslyEx())
                {
                    bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, (int)stream.Length);
                }
            }

            return bytes;
        }

        private const int WriteRetries = 3;

        public override void Write(CacheItemInfo info, byte[] data)
        {
            var fi = new FileItem(info);

            PriorityQueue.AddStorageWorkItem(async () =>
            {
                for (int r = 0; r < WriteRetries; r++)
                {
                    try
                    {
                        await FileItem.EnsurePathAsync(Folder, fi.FileName);
                        using (Stream stream = await Folder.OpenStreamForWriteAsync(fi.FileName, CreationCollisionOption.OpenIfExists))
                        {
                            lock (LockObject)
                            {
                                stream.Write(data, 0, data.Length);
                                stream.Flush();
                            }
                        }
                        lock (LockObject)
                        {
                            _cache[info.UniqueName] = info;
                        }
                        break;
                    }
                    catch (Exception)
                    {
                    }

                    await Task.Delay(50);
                }
            });
        }


        private class FileItem
        {


            private byte[] _data;
            private string _fileName;
            private string _dirName;
            private CacheItemInfo _item;

            public CacheItemInfo Item
            {
                get
                {
                    if (_item == null && _fileName != null)
                    {
                        _item = FromFileName(_fileName);
                    }
                    Debug.Assert(_item != null, "No CacheItemInfo!");
                    return _item;
                }
                private set
                {
                    _item = value;
                }
            }

            public DateTime WriteTime;

            public byte[] Data
            {
                get
                {
                    return _data;
                }
                set
                {
                    if (_data != value)
                    {
                        _data = value;
                        WriteTime = DateTime.Now;
                    }
                }
            }

            public string DirectoryName
            {
                get
                {
                    if (_dirName == null)
                    {
                        _dirName = Item.UniqueName.GetHashCode().ToString();
                    }
                    return _dirName;
                }
            }

            public string FileName
            {
                get
                {
                    if (_fileName == null)
                    {
                        _fileName = ToFileName(Item);
                    }
                    return _fileName;
                }
            }

            public int Length
            {
                get
                {
                    if (_data == null)
                    {
                        return 0;
                    }
                    return _data.Length;
                }
            }

            public FileItem(string fileName)
            {
                _fileName = fileName;
            }

            public FileItem(CacheItemInfo item)
            {
                Item = item;
            }

            public override bool Equals(object obj)
            {
                var other = (FileItem)obj;

                if (_fileName != null)
                {
                    return other._fileName == _fileName;
                }
                else if (_item != null)
                {
                    return Object.Equals(_item, other._item);
                }
                return false;
            }

            public override int GetHashCode()
            {
                return FileName.GetHashCode();
            }

#if DEBUG

            public override string ToString()
            {
                return FileName;
            }
#endif

            public static async Task EnsurePathAsync(StorageFolder folder, string filename)
            {

                for (string path = Path.GetDirectoryName(filename);
                            path != "";
                            path = Path.GetDirectoryName(path))
                {

                    await folder.CreateFolderAsync(path, CreationCollisionOption.OpenIfExists);
                }
            }

            public static string DirectoryHash(string uniqueName)
            {
                return Path.Combine(CacheDirectoryPrefix, uniqueName.GetHashCode().ToString());
            }

            public static CacheItemInfo FromFileName(string fileName)
            {
                if (!fileName.StartsWith(CacheDirectoryPrefix))
                {

                    fileName = Path.GetFileName(fileName);

                    string[] parts = fileName
                        .Split(FileNameSeparator);

                    if (parts.Length == 4)
                    {

                        string uniqueKey = DecodePathName(parts[0]);

                        var item = new CacheItemInfo(uniqueKey)
                        {
                            ExpirationTime = new DateTime(Int64.Parse(parts[2])),
                            UpdatedTime = new DateTime(Int64.Parse(parts[3])),
                            IsOptimized = Boolean.Parse(parts[1])
                        };

                        return item;
                    }
                }
                return null;
            }

            private static string DecodePathName(string encodedPath)
            {
                return Uri.UnescapeDataString(encodedPath);
            }

            private static string EncodePathName(string path)
            {

                return Uri.EscapeDataString(path);
            }

            private static string ToFileName(CacheItemInfo item)
            {
                string name = EncodePathName(item.UniqueName);
                name = String.Format("{1}{0}{2}{0}{3}{0}{4}", FileNameSeparator, name, item.IsOptimized, item.ExpirationTime.Ticks, item.UpdatedTime.Ticks);
                name = Path.Combine(DirectoryHash(item.UniqueName), name);
                return name;
            }
        }
    }


}
