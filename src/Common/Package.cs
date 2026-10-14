using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;

namespace DKSelfExtractor
{
    internal sealed class PackageInfo
    {
        internal string Name, Destination, Note, PostCommand;
        internal byte CommandShell;
        internal bool Encrypted;
        internal byte[] Salt, IV, Mac, AuthenticatedHeader;
        internal long Offset, Length;
    }

    internal static class Package
    {
        private const string Magic = "DKSFX001";
        private const int FooterSize = 20;
        private const int Iterations = 150000;

        internal static string TempPath()
        {
            return Path.Combine(Path.GetTempPath(), "dksfx-" + Guid.NewGuid().ToString("N") + ".tmp");
        }

        internal static void Copy(Stream from, Stream to)
        {
            byte[] buffer = new byte[131072];
            int count;
            while ((count = from.Read(buffer, 0, buffer.Length)) > 0)
                to.Write(buffer, 0, count);
        }

        private static void CheckDirectory(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("연결 또는 재분석 지점 폴더는 포함할 수 없습니다: " + directory);
        }

        private static void AddFolder(ZipArchive archive, string root, string current,
                                      string prefix, CompressionLevel level)
        {
            CheckDirectory(current);
            foreach (string directory in Directory.GetDirectories(current))
            {
                CheckDirectory(directory);
                string relative = directory.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                archive.CreateEntry((prefix.Length == 0 ? relative : prefix + "/" + relative) + "/");
                AddFolder(archive, root, directory, prefix, level);
            }
            foreach (string file in Directory.GetFiles(current))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("연결 또는 재분석 지점 파일은 포함할 수 없습니다: " + file);
                string relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                archive.CreateEntryFromFile(file, prefix.Length == 0 ? relative : prefix + "/" + relative, level);
            }
        }

        internal static void CreateZip(string[] sourcePaths, string path, CompressionLevel level)
        {
            if (sourcePaths == null || sourcePaths.Length == 0)
                throw new ArgumentException("추가할 파일 또는 폴더가 없습니다.");

            // A single selected folder contributes only its contents.
            // Multiple sources retain their top-level names without silent collisions.
            bool singleFolder = sourcePaths.Length == 1 && Directory.Exists(Path.GetFullPath(sourcePaths[0]));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (string source in sourcePaths)
                {
                    string full = Path.GetFullPath(source);
                    string baseName = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (String.IsNullOrEmpty(baseName) || baseName.Contains(":"))
                        throw new ArgumentException("드라이브의 루트 폴더는 추가할 수 없습니다: " + source);
                    if (!names.Add(baseName))
                        throw new ArgumentException("동일한 이름의 최상위 파일/폴더를 함께 추가할 수 없습니다: " + baseName);

                    if (Directory.Exists(full))
                    {
                        CheckDirectory(full);
                        if (!singleFolder) archive.CreateEntry(baseName + "/");
                        AddFolder(archive, full.TrimEnd(Path.DirectorySeparatorChar), full,
                                  singleFolder ? "" : baseName, level);
                    }
                    else if (File.Exists(full))
                    {
                        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                            throw new InvalidDataException("연결 또는 재분석 지점 파일은 포함할 수 없습니다: " + full);
                        archive.CreateEntryFromFile(full, baseName, level);
                    }
                    else throw new FileNotFoundException("추가한 파일 또는 폴더를 찾을 수 없습니다.", full);
                }
            }
        }

        private static byte[] Random(int size)
        {
            var value = new byte[size];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(value);
            return value;
        }

        private static byte[] Keys(string password, byte[] salt)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(64);
        }

        private static byte[] Header(string name, string destination, string note, bool encrypted, byte[] salt, byte[] iv,
                                     byte commandShell, string postCommand)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(String.IsNullOrWhiteSpace(postCommand) ? 1 : 2);
                writer.Write(name);
                writer.Write(destination);
                writer.Write(note);
                writer.Write(encrypted);
                writer.Write((byte)salt.Length);
                writer.Write(salt);
                writer.Write((byte)iv.Length);
                writer.Write(iv);
                if (!String.IsNullOrWhiteSpace(postCommand))
                {
                    if (commandShell != 1 && commandShell != 2)
                        throw new ArgumentException("지원되지 않는 명령줄 유형입니다.");
                    writer.Write(commandShell);
                    writer.Write(postCommand);
                }
                writer.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] Hmac(string ciphertext, byte[] iv, byte[] header, byte[] key)
        {
            using (var hmac = new HMACSHA256(key))
            using (var input = File.OpenRead(ciphertext))
            {
                hmac.TransformBlock(iv, 0, iv.Length, iv, 0);
                byte[] buffer = new byte[131072];
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    hmac.TransformBlock(buffer, 0, n, buffer, 0);
                hmac.TransformFinalBlock(header, 0, header.Length);
                return hmac.Hash;
            }
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            int diff = 0;
            for (int i = 0; i < left.Length; i++) diff |= left[i] ^ right[i];
            return diff == 0;
        }

        internal static void WriteExe(string stubFile, string zipFile, string output, string name,
                                      string destination, string note, string password,
                                      byte commandShell, string postCommand)
        {
            bool encrypted = !String.IsNullOrEmpty(password);
            byte[] salt = encrypted ? Random(16) : new byte[0];
            byte[] iv = encrypted ? Random(16) : new byte[0];
            byte[] mac = new byte[0];
            byte[] header = Header(name, destination, note, encrypted, salt, iv,
                                   commandShell, postCommand);
            string cipherFile = null;
            string payload = zipFile;
            try
            {
                if (encrypted)
                {
                    cipherFile = TempPath();
                    byte[] keys = Keys(password, salt);
                    using (var aes = Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;
                        aes.Key = keys.Take(32).ToArray();
                        aes.IV = iv;
                        using (var input = File.OpenRead(zipFile))
                        using (var outputFile = new FileStream(cipherFile, FileMode.CreateNew, FileAccess.Write))
                        using (var crypto = new CryptoStream(outputFile, aes.CreateEncryptor(), CryptoStreamMode.Write))
                            Copy(input, crypto);
                    }
                    payload = cipherFile;
                    mac = Hmac(cipherFile, iv, header, keys.Skip(32).ToArray());
                    Array.Clear(keys, 0, keys.Length);
                }

                using (var destinationFile = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                {
                    using (var stub = File.OpenRead(stubFile)) Copy(stub, destinationFile);
                    using (var archive = File.OpenRead(payload)) Copy(archive, destinationFile);
                    byte[] metadata;
                    using (var ms = new MemoryStream())
                    using (var writer = new BinaryWriter(ms, Encoding.UTF8))
                    {
                        writer.Write(header);
                        writer.Write((byte)mac.Length);
                        writer.Write(mac);
                        writer.Flush();
                        metadata = ms.ToArray();
                    }
                    using (var writer = new BinaryWriter(destinationFile, Encoding.UTF8))
                    {
                        writer.Write(metadata);
                        writer.Write(Encoding.ASCII.GetBytes(Magic));
                        writer.Write(new FileInfo(payload).Length);
                        writer.Write(metadata.Length);
                    }
                }
            }
            finally { if (cipherFile != null && File.Exists(cipherFile)) File.Delete(cipherFile); }
        }

        internal static PackageInfo Read(string executable)
        {
            using (var stream = File.OpenRead(executable))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (stream.Length < FooterSize) throw new InvalidDataException("올바른 자동 압축 해제 파일이 아닙니다.");
                stream.Seek(-FooterSize, SeekOrigin.End);
                string magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
                long payloadLength = reader.ReadInt64();
                int metadataLength = reader.ReadInt32();
                long offset = stream.Length - FooterSize - metadataLength - payloadLength;
                if (magic != Magic || payloadLength <= 0 || metadataLength < 8 || metadataLength > 65536 || offset < 0)
                    throw new InvalidDataException("자동 압축 해제 데이터가 손상되었습니다.");
                stream.Position = offset + payloadLength;
                byte[] bytes = reader.ReadBytes(metadataLength);
                if (bytes.Length != metadataLength) throw new InvalidDataException("메타데이터를 읽을 수 없습니다.");
                var info = new PackageInfo { Offset = offset, Length = payloadLength };
                using (var ms = new MemoryStream(bytes))
                using (var meta = new BinaryReader(ms, Encoding.UTF8))
                {
                    int format = meta.ReadInt32();
                    if (format != 1 && format != 2)
                        throw new InvalidDataException("지원되지 않는 파일 형식입니다.");
                    info.Name = meta.ReadString();
                    info.Destination = meta.ReadString();
                    info.Note = meta.ReadString();
                    info.Encrypted = meta.ReadBoolean();
                    int saltLength = meta.ReadByte();
                    if (saltLength > 32) throw new InvalidDataException("잘못된 암호화 정보입니다.");
                    info.Salt = meta.ReadBytes(saltLength);
                    int ivLength = meta.ReadByte();
                    if (ivLength > 32) throw new InvalidDataException("잘못된 암호화 정보입니다.");
                    info.IV = meta.ReadBytes(ivLength);
                    if (format == 2)
                    {
                        info.CommandShell = meta.ReadByte();
                        info.PostCommand = meta.ReadString();
                        if ((info.CommandShell != 1 && info.CommandShell != 2) ||
                            String.IsNullOrWhiteSpace(info.PostCommand))
                            throw new InvalidDataException("잘못된 작업 후 명령줄입니다.");
                    }
                    else
                    {
                        info.CommandShell = 0;
                        info.PostCommand = "";
                    }
                    info.AuthenticatedHeader = bytes.Take((int)ms.Position).ToArray();
                    int macLength = meta.ReadByte();
                    if (macLength > 32) throw new InvalidDataException("잘못된 암호화 정보입니다.");
                    info.Mac = meta.ReadBytes(macLength);
                    if (ms.Position != ms.Length || (info.Encrypted && (saltLength != 16 || ivLength != 16 || macLength != 32)) ||
                        (!info.Encrypted && (saltLength != 0 || ivLength != 0 || macLength != 0)))
                        throw new InvalidDataException("잘못된 메타데이터입니다.");
                }
                return info;
            }
        }

        internal static string UnpackPayload(string exe, PackageInfo info, string password)
        {
            string downloaded = TempPath();
            string decrypted = null;
            try
            {
                using (var input = File.OpenRead(exe))
                using (var output = new FileStream(downloaded, FileMode.CreateNew, FileAccess.Write))
                {
                    input.Position = info.Offset;
                    long left = info.Length;
                    byte[] buffer = new byte[131072];
                    while (left > 0)
                    {
                        int n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                        if (n <= 0) throw new EndOfStreamException();
                        output.Write(buffer, 0, n);
                        left -= n;
                    }
                }
                if (!info.Encrypted) return downloaded;
                byte[] keys = Keys(password, info.Salt);
                try
                {
                    byte[] observed = Hmac(downloaded, info.IV, info.AuthenticatedHeader, keys.Skip(32).ToArray());
                    if (!Equal(observed, info.Mac))
                        throw new InvalidDataException("암호가 틀리거나 압축 파일이 손상되었습니다.");
                    decrypted = TempPath();
                    using (var aes = Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;
                        aes.Key = keys.Take(32).ToArray();
                        aes.IV = info.IV;
                        using (var input = File.OpenRead(downloaded))
                        using (var crypto = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read))
                        using (var output = new FileStream(decrypted, FileMode.CreateNew, FileAccess.Write))
                            Copy(crypto, output);
                    }
                    File.Delete(downloaded);
                    return decrypted;
                }
                finally { Array.Clear(keys, 0, keys.Length); }
            }
            catch
            {
                if (File.Exists(downloaded)) File.Delete(downloaded);
                if (decrypted != null && File.Exists(decrypted)) File.Delete(decrypted);
                throw;
            }
        }

        private static void SafeParents(string root, string path)
        {
            string cursor = Path.GetDirectoryName(path);
            while (cursor != null && cursor.Length >= root.Length)
            {
                if (Directory.Exists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("연결된 폴더로 압축 해제할 수 없습니다: " + cursor);
                if (String.Equals(cursor, root, StringComparison.OrdinalIgnoreCase)) break;
                string next = Path.GetDirectoryName(cursor);
                if (next == cursor) break;
                cursor = next;
            }
        }

        internal static void Extract(string archivePath, string destination, bool overwrite, Action<int, int> progress)
        {
            string root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(destination));
            string driveRoot = Path.GetPathRoot(root);
            if (!String.Equals(root, driveRoot, StringComparison.OrdinalIgnoreCase))
                root = root.TrimEnd('\\', '/');
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("압축 해제 폴더를 입력하세요.");
            string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? root : root + Path.DirectorySeparatorChar;
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            using (var file = File.OpenRead(archivePath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                int completed = 0;
                foreach (var entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (String.IsNullOrEmpty(name) || name.StartsWith("/") || name.StartsWith("\\") ||
                        name.Contains(":") || name.Contains("\\"))
                        throw new InvalidDataException("허용되지 않는 압축 항목 경로: " + name);
                    string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("압축 해제 폴더를 벗어나는 항목: " + name);
                    SafeParents(root, target);
                    if (name.EndsWith("/"))
                    {
                        Directory.CreateDirectory(target);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        SafeParents(root, target);
                        if (!File.Exists(target) || overwrite)
                        {
                            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                                throw new InvalidDataException("연결된 파일을 덮어쓸 수 없습니다: " + target);
                            entry.ExtractToFile(target, overwrite);
                        }
                    }
                    completed++;
                    if (progress != null) progress(completed, archive.Entries.Count);
                }
            }
        }
    }
}
