using System;
using System.IO;
using UnityEngine;

namespace EchoSpace.VoiceInput
{
    public static class WavUtility
    {
        public static byte[] FromAudioClip(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogError("[VoiceInput] WavUtility.FromAudioClip received null AudioClip.");
                return Array.Empty<byte>();
            }

            int sampleCount = clip.samples * clip.channels;
            float[] samples = new float[sampleCount];
            clip.GetData(samples, 0);

            short[] intData = new short[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                intData[i] = (short)(clamped * short.MaxValue);
            }

            int byteCount = sampleCount * sizeof(short);
            byte[] dataBytes = new byte[byteCount];
            Buffer.BlockCopy(intData, 0, dataBytes, 0, byteCount);

            using (MemoryStream stream = new MemoryStream(44 + byteCount))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int sampleRate = clip.frequency;
                short channels = (short)clip.channels;
                short bitsPerSample = 16;
                int byteRate = sampleRate * channels * (bitsPerSample / 8);
                short blockAlign = (short)(channels * (bitsPerSample / 8));

                // RIFF header
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + byteCount);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

                // fmt chunk
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write(bitsPerSample);

                // data chunk
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(byteCount);
                writer.Write(dataBytes);

                return stream.ToArray();
            }
        }
    }
}
