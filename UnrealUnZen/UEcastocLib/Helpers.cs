using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UEcastocLib
{
    public static class Helpers
    {

        public static byte[] DecryptAES(byte[] ciphertext, byte[] AESKey)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = AESKey;
                aesAlg.Mode = CipherMode.ECB;
                aesAlg.Padding = PaddingMode.Zeros;

                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                byte[] decryptedBytes = new byte[ciphertext.Length];

                using (MemoryStream msDecrypt = new MemoryStream(ciphertext))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        csDecrypt.Read(decryptedBytes, 0, decryptedBytes.Length);
                    }
                }

                return decryptedBytes;
            }
        }

        public static byte[] EncryptAES(byte[] plaintext, byte[] AES)
        {
            using (AesManaged aes = new AesManaged())
            {
                aes.Key = AES;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                {
                    return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }
            }
        }

        private const int Gigabyte = 1073741824;

        public static void EncryptFileWithAES(string filePath, byte[] aesKey, int numberOfBytesToWorkOnAtATime = Gigabyte)
        {
            using (FileStream streamIn = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                using (FileStream streamOut = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    EncryptWithAES(streamIn, streamOut, aesKey, numberOfBytesToWorkOnAtATime);
                }
            }
        }
        
        public static void EncryptWithAES(Stream streamIn, Stream streamOut, byte[] aesKey, int numberOfBytesToWorkOnAtATime = Gigabyte)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = aesKey;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                {
                    CryptoStream EncryptionStream = new CryptoStream(streamOut, encryptor, CryptoStreamMode.Write);

                    byte[] buffer = new byte[numberOfBytesToWorkOnAtATime];
                    long bytesWrittenTotal = 0;
                    long totalLengthOfInputStream = streamIn.Length;

                    //Read from the input file, then encrypt and write to the output file.
                    while (bytesWrittenTotal < totalLengthOfInputStream)
                    {
                        int totalNumberOfBytesRead = streamIn.Read(buffer, 0, numberOfBytesToWorkOnAtATime);
                        EncryptionStream.Write(buffer, 0, totalNumberOfBytesRead);
                        bytesWrittenTotal += totalNumberOfBytesRead;
                    }
                }
            }
        }

        public static byte[] HexStringToByteArray(string hexString)
        {
            if (hexString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hexString = hexString.Substring(2);
            }

            if (hexString.Length % 2 != 0)
            {
                throw new ArgumentException("Hexadecimal string length must be even.");
            }

            byte[] byteArray = new byte[hexString.Length / 2];

            for (int i = 0; i < byteArray.Length; i++)
            {
                byteArray[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            }

            return byteArray;
        }

        public static byte[] SHA1Hash(byte[] data)
        {
            using (SHA1Managed sha1 = new SHA1Managed())
            {
                return sha1.ComputeHash(data);
            }
        }
        public static byte[] SHA1Hash(this MemoryMappedFile mmf)
        {
            using (MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor())
            {
                long length = accessor.Capacity;
                int bufferSize = 4096;
                byte[] buffer = new byte[bufferSize];

                using (SHA1 sha1 = SHA1.Create())
                {
                    long position = 0;
                    while (position < length)
                    {
                        int bytesToRead = (int)Math.Min(bufferSize, length - position);

                        accessor.ReadArray(position, buffer, 0, bytesToRead);
                        sha1.TransformBlock(buffer, 0, bytesToRead, buffer, 0);

                        position += bytesToRead;
                    }

                    sha1.TransformFinalBlock(buffer, 0, 0);
                    return sha1.Hash;
                }
            }
        }

        public static UInt64 RandomUlong()
        {
            Random rnd = new Random();
            int rawsize = System.Runtime.InteropServices.Marshal.SizeOf(ulong.MaxValue);
            var buffer = new byte[rawsize];
            rnd.NextBytes(buffer);
            return BitConverter.ToUInt64(buffer, 0);
        }
        public static byte[] GetRandomBytes(int n)
        {
            byte[] ret = new byte[n];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(ret);
            }
            return ret;
        }

        public static byte[] StringToFString(string str)
        {
            byte[] strBytes = Encoding.ASCII.GetBytes(str);
            byte[] strlenBytes = BitConverter.GetBytes((uint)(str.Length + 1));
            byte[] fstring = new byte[strlenBytes.Length + strBytes.Length + 1];
            Array.Copy(strlenBytes, fstring, strlenBytes.Length);
            Array.Copy(strBytes, 0, fstring, strlenBytes.Length, strBytes.Length);
            fstring[fstring.Length - 1] = 0;
            return fstring;
        }
        public static string ReadFString(this BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length > 0)
            {
                byte[] strBytes = reader.ReadBytes(length);
                return Encoding.UTF8.GetString(strBytes, 0, strBytes.Length - 1);
            }
            else if (length < 0)
            {
                length *= -2;
                byte[] strBytes = reader.ReadBytes(length);
                return Encoding.Unicode.GetString(strBytes, 0, strBytes.Length - 2);
            }
            else
                return "";
        }
        public static byte[] UInt32ToBytes(uint a)
        {
            return BitConverter.GetBytes(a);
        }

        public static long Align(uint ptr, int alignment)
        {
            return ptr + alignment - 1 & ~(alignment - 1);
        }
        public static byte[] ReadBytesOfFile(this MemoryMappedFile mmf, long offset, long length)
        {
            using (MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(offset, length))
            {
                byte[] data = new byte[length];
                accessor.ReadArray(0, data, 0, data.Length);
                return data;
            }
        }
        public static MemoryMappedFile CreateMemoryMappedFileFromByteArray(byte[] data, string filePath)
        {
            using (MemoryMappedFile mmf = MemoryMappedFile.CreateNew(Path.GetFileNameWithoutExtension(filePath), data.Length))
            {
                using (MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor())
                {
                    accessor.WriteArray(0, data, 0, data.Length);
                }
                return MemoryMappedFile.OpenExisting(Path.GetFileNameWithoutExtension(filePath));
            }
        }
    }
}
