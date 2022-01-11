using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Allows for saving audio clips to wav file.
/// Code found here: https://gist.github.com/darktable/2317063
/// </summary>
public static class SavWav
{

	const int HEADER_SIZE = 44;
	struct ClipData
	{

		public int samples;
		public int channels;
		public float[] samplesData;

	}

	public static bool Save(string path, AudioClip clip)
	{
		var assetPath = path.Trim('/') + "/" + clip.name + ".wav";
		var filepath = Directory.GetParent(Application.dataPath) + "/" + path.Trim('/') + "/" + clip.name + ".wav";

		Debug.Log(filepath);
		Debug.Log(assetPath);

		AssetDatabase.DeleteAsset(assetPath);

		// Make sure directory exists if user is saving to sub dir.
		ClipData clipdata = new ClipData();
		clipdata.samples = clip.samples;
		clipdata.channels = clip.channels;
		float[] dataFloat = new float[clip.samples * clip.channels];
		clip.GetData(dataFloat, 0);
		clipdata.samplesData = dataFloat;

		using (var fileStream = CreateEmpty(filepath))
		{
			MemoryStream memstrm = new MemoryStream();
			ConvertAndWrite(memstrm, clipdata);
			memstrm.WriteTo(fileStream);
			WriteHeader(fileStream, clip);
		}

		AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
		AssetDatabase.Refresh();

		return true;
	}

	public static AudioClip TrimSilence(AudioClip clip, float min)
	{
		var samples = new float[clip.samples];

		clip.GetData(samples, 0);

		return TrimSilence(new List<float>(samples), min, clip.channels, clip.frequency);
	}

	public static AudioClip TrimSilence(List<float> samples, float min, int channels, int hz)
	{
		return TrimSilence(samples, min, channels, hz, false, false);
	}

	public static AudioClip TrimSilence(List<float> samples, float min, int channels, int hz, bool _3D, bool stream)
	{
		int i;

		for (i = 0; i < samples.Count; i++)
		{
			if (Mathf.Abs(samples[i]) > min)
			{
				break;
			}
		}

		samples.RemoveRange(0, i);

		for (i = samples.Count - 1; i > 0; i--)
		{
			if (Mathf.Abs(samples[i]) > min)
			{
				break;
			}
		}

		samples.RemoveRange(i, samples.Count - i);
		
#pragma warning disable 0618
		var clip = AudioClip.Create("TempClip", samples.Count, channels, hz, _3D, stream);
#pragma warning restore 0618

		clip.SetData(samples.ToArray(), 0);

		return clip;
	}

	static FileStream CreateEmpty(string filepath)
	{
		var fileStream = new FileStream(filepath, FileMode.Create);
		byte emptyByte = new byte();

		for (int i = 0; i < HEADER_SIZE; i++) //preparing the header
		{
			fileStream.WriteByte(emptyByte);
		}

		return fileStream;
	}

	static void ConvertAndWrite(MemoryStream memStream, ClipData clipData)
	{
		float[] samples = new float[clipData.samples * clipData.channels];

		samples = clipData.samplesData;

		Int16[] intData = new Int16[samples.Length];

		Byte[] bytesData = new Byte[samples.Length * 2];

		const float rescaleFactor = 32767; //to convert float to Int16

		for (int i = 0; i < samples.Length; i++)
		{
			intData[i] = (short)(samples[i] * rescaleFactor);
			//Debug.Log (samples [i]);
		}
		Buffer.BlockCopy(intData, 0, bytesData, 0, bytesData.Length);
		memStream.Write(bytesData, 0, bytesData.Length);
	}

	static void WriteHeader(FileStream fileStream, AudioClip clip)
	{

		var hz = clip.frequency;
		var channels = clip.channels;
		var samples = clip.samples;

		fileStream.Seek(0, SeekOrigin.Begin);

		Byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
		fileStream.Write(riff, 0, 4);

		Byte[] chunkSize = BitConverter.GetBytes(fileStream.Length - 8);
		fileStream.Write(chunkSize, 0, 4);

		Byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
		fileStream.Write(wave, 0, 4);

		Byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
		fileStream.Write(fmt, 0, 4);

		Byte[] subChunk1 = BitConverter.GetBytes(16);
		fileStream.Write(subChunk1, 0, 4);
		
		UInt16 one = 1;

		Byte[] audioFormat = BitConverter.GetBytes(one);
		fileStream.Write(audioFormat, 0, 2);

		Byte[] numChannels = BitConverter.GetBytes(channels);
		fileStream.Write(numChannels, 0, 2);

		Byte[] sampleRate = BitConverter.GetBytes(hz);
		fileStream.Write(sampleRate, 0, 4);

		Byte[] byteRate = BitConverter.GetBytes(hz * channels * 2); // sampleRate * bytesPerSample*number of channels, here 44100*2*2
		fileStream.Write(byteRate, 0, 4);

		UInt16 blockAlign = (ushort)(channels * 2);
		fileStream.Write(BitConverter.GetBytes(blockAlign), 0, 2);

		UInt16 bps = 16;
		Byte[] bitsPerSample = BitConverter.GetBytes(bps);
		fileStream.Write(bitsPerSample, 0, 2);

		Byte[] datastring = System.Text.Encoding.UTF8.GetBytes("data");
		fileStream.Write(datastring, 0, 4);

		Byte[] subChunk2 = BitConverter.GetBytes(samples * channels * 2);
		fileStream.Write(subChunk2, 0, 4);

		//		fileStream.Close();
	}
}
