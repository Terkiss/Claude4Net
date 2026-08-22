using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Claude4Net.Runtime.ApiServer
{
    /// <summary>
    /// Embedding vector conversion utilities (float[] ↔ Base64).
    /// </summary>
    public static class EmbeddingUtils
    {
        public static string FloatsToBase64(IList<float> floats)
        {
            var bytes = new byte[floats.Count * sizeof(float)];
            for (int i = 0; i < floats.Count; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)), floats[i]);
            }
            return Convert.ToBase64String(bytes);
        }

        public static float[] Base64ToFloats(string base64)
        {
            var bytes = Convert.FromBase64String(base64);
            var floats = new float[bytes.Length / sizeof(float)];
            for (int i = 0; i < floats.Length; i++)
            {
                floats[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
            }
            return floats;
        }
    }
}
