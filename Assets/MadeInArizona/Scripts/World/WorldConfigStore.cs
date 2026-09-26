using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Persists editable world-generation settings and only publishes complete,
    /// validated changes. Call TryReload from a regular update loop for hot reload.
    /// </summary>
    public static class WorldConfigStore
    {
        const string FileName = "world-generation.json";
        const string SmokeDirectoryName = "IntegrationTestWorld";
        const int MaximumFileBytes = 1024 * 1024;
        const double ReloadPollSeconds = 1d;

        static WorldGenConfig lastValid;
        static FileStamp lastObserved;
        static DateTime nextPollUtc;

        public static string DirectoryPath
        {
            get { return SmokeTestRunner.Active ? System.IO.Path.Combine(Application.temporaryCachePath, SmokeDirectoryName) : Application.persistentDataPath; }
        }

        public static string Path { get { return System.IO.Path.Combine(DirectoryPath, FileName); } }
        public static string LastError { get; private set; }
        public static WorldGenConfig LastValid { get { return lastValid; } }

        /// <summary>Loads the saved config, creating a documented default when absent.</summary>
        public static WorldGenConfig Load()
        {
            lastValid = null;
            LastError = null;
            nextPollUtc = DateTime.MinValue;

            if (!File.Exists(Path))
            {
                WorldGenConfig defaults = new WorldGenConfig();
                string validationError=null;
                if (!defaults.Validate(out validationError)) throw new InvalidOperationException("World config defaults are invalid: " + validationError);
                lastValid = defaults;
                Save(defaults);
                lastObserved = ReadStamp(Path);
                return lastValid;
            }

            WorldGenConfig loaded;
            string error;
            if (TryRead(Path, out loaded, out error))
            {
                lastValid = loaded;
                lastObserved = ReadStamp(Path);
                return lastValid;
            }

            LastError = error;
            lastValid = new WorldGenConfig();
            lastObserved = ReadStamp(Path);
            return lastValid;
        }

        /// <summary>Writes a validated config through a temporary file then atomically replaces the destination.</summary>
        public static bool Save(WorldGenConfig config)
        {
            string validationError=null;
            if (config == null || !config.Validate(out validationError))
            {
                LastError = "Could not save world config: " + (validationError ?? "config cannot be null.");
                return false;
            }

            string temporaryPath = Path + ".tmp";
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(config, true));
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(Path))
                {
                    try { File.Replace(temporaryPath, Path, null); }
                    catch (PlatformNotSupportedException) { ReplaceFallback(temporaryPath); }
                    catch (IOException) { ReplaceFallback(temporaryPath); }
                }
                else File.Move(temporaryPath, Path);

                lastValid = config;
                lastObserved = ReadStamp(Path);
                LastError = null;
                return true;
            }
            catch (Exception exception)
            {
                LastError = "Could not save world config: " + exception.Message;
                Debug.LogWarning(LastError);
                return false;
            }
        }

        /// <summary>
        /// Polls once per second. Returns true only when a changed file parses and
        /// validates; malformed or partial edits leave the active config untouched.
        /// </summary>
        public static bool TryReload(out WorldGenConfig config)
        {
            config = lastValid;
            DateTime now = DateTime.UtcNow;
            if (now < nextPollUtc) return false;
            nextPollUtc = now.AddSeconds(ReloadPollSeconds);

            FileStamp current = ReadStamp(Path);
            if (current.Equals(lastObserved)) return false;
            lastObserved = current;
            if (!current.exists)
            {
                LastError = "World config was removed; keeping the last valid config.";
                return false;
            }

            WorldGenConfig candidate;
            string error;
            if (!TryRead(Path, out candidate, out error))
            {
                LastError = error;
                return false;
            }

            lastValid = candidate;
            config = candidate;
            LastError = null;
            return true;
        }

        static bool TryRead(string path, out WorldGenConfig config, out string error)
        {
            config = null;
            error = null;
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists) throw new FileNotFoundException("File does not exist.", path);
                if (info.Length == 0) throw new InvalidDataException("File is empty.");
                if (info.Length > MaximumFileBytes) throw new InvalidDataException("File exceeds the 1 MB size limit.");
                string json = File.ReadAllText(path, Encoding.UTF8);
                RequireGenerationFields(json);
                config = JsonUtility.FromJson<WorldGenConfig>(json);
                // Configs written before the seven-town minimum are raised rather than rejected.
                if (config != null && config.townCount >= 2 && config.townCount < WorldGenConfig.MinTowns) config.townCount = WorldGenConfig.MinTowns;
                string validationError=null;
                if (config == null || !config.Validate(out validationError)) throw new InvalidDataException(validationError ?? "JSON did not contain a world config.");
                return true;
            }
            catch (Exception exception)
            {
                config = null;
                error = "Could not load world config: " + exception.Message;
                return false;
            }
        }

        // JsonUtility ignores unknown members and can supply field initializers for
        // omitted members. Requiring the generation inputs prevents a partially
        // written object such as "{}" from silently becoming a new default world.
        static void RequireGenerationFields(string json)
        {
            string[] fields = { "seed", "townCount", "poiCount", "size", "terrainHeight", "vegetation", "riverWidth" };
            for (int i = 0; i < fields.Length; i++)
            {
                if (json.IndexOf("\"" + fields[i] + "\"", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("Missing required field '" + fields[i] + "'.");
            }
        }

        static void ReplaceFallback(string temporaryPath)
        {
            // Preserve the original rather than exposing a partially replaced config.
            throw new IOException("Atomic config replacement is unavailable; the original file was preserved.");
        }

        static FileStamp ReadStamp(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                return new FileStamp { exists = info.Exists, length = info.Exists ? info.Length : 0L, writeTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0L };
            }
            catch (Exception) { return default(FileStamp); }
        }

        struct FileStamp : IEquatable<FileStamp>
        {
            public bool exists;
            public long length;
            public long writeTicks;
            public bool Equals(FileStamp other) { return exists == other.exists && length == other.length && writeTicks == other.writeTicks; }
        }
    }
}
