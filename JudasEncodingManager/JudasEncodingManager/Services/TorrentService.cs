using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace JudasEncodingManager.Services
{
    public class TorrentService
    {
        private readonly string _announceUrl = "http://nyaa.tracker.wf:7777/announce";
        private string _mktorrentPath = "";

        public void Configure(string mktorrentPath = "")
        {
            _mktorrentPath = mktorrentPath;
        }

        public async Task<TorrentCreateResult> CreateTorrentAsync(string inputPath, string outputTorrentPath, string? comment = null)
        {
            var result = new TorrentCreateResult { InputPath = inputPath, OutputPath = outputTorrentPath };

            try
            {
                // Try using mktorrent first if available
                if (!string.IsNullOrEmpty(_mktorrentPath) && File.Exists(_mktorrentPath))
                {
                    result = await CreateWithMktorrentAsync(inputPath, outputTorrentPath, comment);
                }
                else
                {
                    // Fall back to manual creation
                    result = await CreateTorrentManuallyAsync(inputPath, outputTorrentPath, comment);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<TorrentCreateResult> CreateWithMktorrentAsync(string inputPath, string outputTorrentPath, string? comment)
        {
            var result = new TorrentCreateResult { InputPath = inputPath, OutputPath = outputTorrentPath };

            var arguments = $"-a \"{_announceUrl}\" -p -l 20";
            if (!string.IsNullOrEmpty(comment))
            {
                arguments += $" -c \"{comment}\"";
            }
            arguments += $" -o \"{outputTorrentPath}\" \"{inputPath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = _mktorrentPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                result.Success = false;
                result.ErrorMessage = "Failed to start mktorrent";
                return result;
            }

            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(outputTorrentPath))
            {
                result.Success = true;
                result.InfoHash = await CalculateInfoHashAsync(outputTorrentPath);
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = await process.StandardError.ReadToEndAsync();
            }

            return result;
        }

        private async Task<TorrentCreateResult> CreateTorrentManuallyAsync(string inputPath, string outputTorrentPath, string? comment)
        {
            var result = new TorrentCreateResult { InputPath = inputPath, OutputPath = outputTorrentPath };

            try
            {
                var torrent = new Dictionary<string, object>
                {
                    ["announce"] = _announceUrl,
                    ["created by"] = "Judas Encoding Manager",
                    ["creation date"] = (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };

                if (!string.IsNullOrEmpty(comment))
                {
                    torrent["comment"] = comment;
                }

                var info = new Dictionary<string, object>
                {
                    ["piece length"] = 1048576L // 1MB pieces — no "private" flag so DHT/PEX/LSD are enabled
                };

                if (File.Exists(inputPath))
                {
                    // Single file
                    var fileInfo = new FileInfo(inputPath);
                    info["name"] = fileInfo.Name;
                    info["length"] = fileInfo.Length;
                    info["pieces"] = await CalculatePiecesAsync(inputPath, 1048576);
                }
                else if (Directory.Exists(inputPath))
                {
                    // Directory (multiple files)
                    var dirInfo = new DirectoryInfo(inputPath);
                    info["name"] = dirInfo.Name;

                    var files = new List<Dictionary<string, object>>();
                    var allPieces = new MemoryStream();

                    foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.GetRelativePath(inputPath, file.FullName);
                        var pathParts = relativePath.Split(Path.DirectorySeparatorChar);

                        files.Add(new Dictionary<string, object>
                        {
                            ["length"] = file.Length,
                            ["path"] = new List<string>(pathParts)
                        });

                        var pieces = await CalculatePiecesAsync(file.FullName, 1048576);
                        await allPieces.WriteAsync(pieces);
                    }

                    info["files"] = files;
                    info["pieces"] = allPieces.ToArray();
                }

                torrent["info"] = info;

                // Encode and write
                var encoded = BencodeEncode(torrent);
                await File.WriteAllBytesAsync(outputTorrentPath, encoded);

                result.Success = true;
                result.InfoHash = await CalculateInfoHashAsync(outputTorrentPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<byte[]> CalculatePiecesAsync(string filePath, int pieceLength)
        {
            var pieces = new MemoryStream();

            using var fileStream = File.OpenRead(filePath);
            var buffer = new byte[pieceLength];
            int bytesRead;

            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, pieceLength)) > 0)
            {
                using var sha1 = SHA1.Create();
                var hash = sha1.ComputeHash(buffer, 0, bytesRead);
                await pieces.WriteAsync(hash, 0, hash.Length);
            }

            return pieces.ToArray();
        }

        private async Task<string> CalculateInfoHashAsync(string torrentPath)
        {
            try
            {
                var torrentData = await File.ReadAllBytesAsync(torrentPath);
                var decoded = BencodeDecode(torrentData);

                if (decoded is Dictionary<string, object> dict && dict.ContainsKey("info"))
                {
                    var infoDict = dict["info"];
                    var infoEncoded = BencodeEncode(infoDict);

                    using var sha1 = SHA1.Create();
                    var hash = sha1.ComputeHash(infoEncoded);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to calculate info hash: {ex.Message}");
            }

            return "";
        }

        // Simple bencode encoder
        private byte[] BencodeEncode(object obj)
        {
            using var stream = new MemoryStream();
            BencodeEncodeToStream(obj, stream);
            return stream.ToArray();
        }

        private void BencodeEncodeToStream(object obj, Stream stream)
        {
            var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            switch (obj)
            {
                case string s:
                    var bytes = Encoding.UTF8.GetBytes(s);
                    writer.Write(Encoding.ASCII.GetBytes($"{bytes.Length}:"));
                    writer.Write(bytes);
                    break;

                case byte[] b:
                    writer.Write(Encoding.ASCII.GetBytes($"{b.Length}:"));
                    writer.Write(b);
                    break;

                case long l:
                    writer.Write(Encoding.ASCII.GetBytes($"i{l}e"));
                    break;

                case int i:
                    writer.Write(Encoding.ASCII.GetBytes($"i{i}e"));
                    break;

                case IList<object> list:
                    writer.Write((byte)'l');
                    foreach (var item in list)
                        BencodeEncodeToStream(item, stream);
                    writer.Write((byte)'e');
                    break;

                case List<string> strList:
                    writer.Write((byte)'l');
                    foreach (var item in strList)
                        BencodeEncodeToStream(item, stream);
                    writer.Write((byte)'e');
                    break;

                case Dictionary<string, object> dict:
                    writer.Write((byte)'d');
                    foreach (var kvp in dict.OrderBy(x => x.Key, StringComparer.Ordinal))
                    {
                        BencodeEncodeToStream(kvp.Key, stream);
                        BencodeEncodeToStream(kvp.Value, stream);
                    }
                    writer.Write((byte)'e');
                    break;
            }
        }

        // Simple bencode decoder (basic implementation)
        private object BencodeDecode(byte[] data)
        {
            var index = 0;
            return BencodeDecodeValue(data, ref index);
        }

        private object BencodeDecodeValue(byte[] data, ref int index)
        {
            var c = (char)data[index];

            if (c == 'd')
            {
                return BencodeDecodeDict(data, ref index);
            }
            else if (c == 'l')
            {
                return BencodeDecodeList(data, ref index);
            }
            else if (c == 'i')
            {
                return BencodeDecodeInt(data, ref index);
            }
            else if (char.IsDigit(c))
            {
                return BencodeDecodeString(data, ref index);
            }

            throw new Exception($"Invalid bencode at position {index}");
        }

        private Dictionary<string, object> BencodeDecodeDict(byte[] data, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++; // skip 'd'

            while ((char)data[index] != 'e')
            {
                var key = Encoding.UTF8.GetString(BencodeDecodeString(data, ref index));
                var value = BencodeDecodeValue(data, ref index);
                dict[key] = value;
            }

            index++; // skip 'e'
            return dict;
        }

        private List<object> BencodeDecodeList(byte[] data, ref int index)
        {
            var list = new List<object>();
            index++; // skip 'l'

            while ((char)data[index] != 'e')
            {
                list.Add(BencodeDecodeValue(data, ref index));
            }

            index++; // skip 'e'
            return list;
        }

        private long BencodeDecodeInt(byte[] data, ref int index)
        {
            index++; // skip 'i'
            var endIndex = Array.IndexOf(data, (byte)'e', index);
            var numStr = Encoding.ASCII.GetString(data, index, endIndex - index);
            index = endIndex + 1;
            return long.Parse(numStr);
        }

        private byte[] BencodeDecodeString(byte[] data, ref int index)
        {
            var colonIndex = Array.IndexOf(data, (byte)':', index);
            var lengthStr = Encoding.ASCII.GetString(data, index, colonIndex - index);
            var length = int.Parse(lengthStr);
            index = colonIndex + 1;

            var result = new byte[length];
            Array.Copy(data, index, result, 0, length);
            index += length;

            return result;
        }
    }

    public class TorrentCreateResult
    {
        public bool Success { get; set; }
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public string InfoHash { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }
}
