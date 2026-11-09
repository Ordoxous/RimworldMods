using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ordo.ProcessAudioCapture;

// Records what one process and its children play, and nothing else: no microphone and no other programs. ffmpeg has no way
// to do this, because it needs Windows' process loopback API (Windows 10 2004 or later).
//
// Usage: ProcessAudioCapture <pid> <output.wav>
// Stops when a line is written to standard input. Prints "started <unix ms>" when the first sample of the file is taken.
public static class Program
{
	private const int SampleRate = 48000;

	private const int Channels = 2;

	private const int BytesPerFrame = Channels * 2;

	[MTAThread]
	public static int Main(
		string[] args
	)
	{
		if (args.Length != 2 || !int.TryParse(args[0], out var processId))
		{
			Console.Error.WriteLine("Usage: ProcessAudioCapture <pid> <output.wav>");
			return 2;
		}

		var client = Activate(processId);
		var format = new WaveFormat
		{
			Tag = 1,
			Channels = Channels,
			SamplesPerSecond = SampleRate,
			AverageBytesPerSecond = SampleRate * BytesPerFrame,
			BlockAlign = BytesPerFrame,
			BitsPerSample = 16,
			ExtraSize = 0
		};

		const uint loopback = 0x00020000;
		const uint eventCallback = 0x00040000;
		const uint autoConvert = 0x80000000;
		const uint defaultQuality = 0x08000000;
		const long bufferDuration = 2_000_000;
		Check(client.Initialize(0, loopback | eventCallback | autoConvert | defaultQuality, bufferDuration, 0, ref format, IntPtr.Zero));

		using var bufferReady = new AutoResetEvent(false);
		Check(client.SetEventHandle(bufferReady.SafeWaitHandle.DangerousGetHandle()));

		var captureId = typeof(IAudioCaptureClient).GUID;
		Check(client.GetService(ref captureId, out var service));
		var capture = (IAudioCaptureClient)service;

		var stop = new ManualResetEventSlim(false);
		new Thread(() =>
		{
			Console.In.ReadLine();
			stop.Set();
		}) { IsBackground = true }.Start();

		using var file = new FileStream(args[1], FileMode.Create, FileAccess.Write);
		using var writer = new BinaryWriter(file);
		WriteWaveHeader(writer, 0);

		long written = 0;
		var silence = new byte[BytesPerFrame * SampleRate / 10];
		var buffer = new byte[BytesPerFrame * SampleRate];
		var clock = Stopwatch.StartNew();
		Check(client.Start());
		Console.WriteLine($"started {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
		Console.Out.Flush();

		while (!stop.IsSet)
		{
			bufferReady.WaitOne(20);
			while (true)
			{
				Check(capture.GetNextPacketSize(out var packetFrames));
				if (packetFrames == 0)
				{
					break;
				}

				Check(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _));
				var bytes = (int)frames * BytesPerFrame;
				const uint silent = 2;
				if ((flags & silent) != 0)
				{
					Array.Clear(buffer, 0, bytes);
				}
				else
				{
					Marshal.Copy(data, buffer, 0, bytes);
				}

				writer.Write(buffer, 0, bytes);
				written += frames;
				Check(capture.ReleaseBuffer(frames));
			}

			// Windows sends nothing while the process is quiet, and the file has to keep time with the video through those gaps.
			// A little slack leaves room for packets on their way, and any sound after a gap lands this much early at most.
			var behind = (long)(clock.Elapsed.TotalSeconds * SampleRate) - written - SampleRate * 3 / 100;
			while (behind > 0)
			{
				var frames = (int)Math.Min(behind, silence.Length / BytesPerFrame);
				writer.Write(silence, 0, frames * BytesPerFrame);
				written += frames;
				behind -= frames;
			}
		}

		Check(client.Stop());
		file.Position = 0;
		WriteWaveHeader(writer, written * BytesPerFrame);
		return 0;
	}

	private static IAudioClient Activate(
		int processId
	)
	{
		var parameters = new ActivationParams { Type = 1, TargetProcessId = processId, Mode = 0 };
		var parameterMemory = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
		var variantMemory = Marshal.AllocHGlobal(Marshal.SizeOf<BlobVariant>());
		try
		{
			Marshal.StructureToPtr(parameters, parameterMemory, false);
			const ushort blobType = 65;
			Marshal.StructureToPtr(new BlobVariant { Type = blobType, Size = (uint)Marshal.SizeOf<ActivationParams>(), Data = parameterMemory }, variantMemory, false);

			var handler = new CompletionHandler();
			var clientId = typeof(IAudioClient).GUID;
			Check(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref clientId, variantMemory, handler, out var operation));
			handler.Done.Wait();

			operation.GetActivateResult(out var result, out var activated);
			Check(result);
			return (IAudioClient)activated;
		}
		finally
		{
			Marshal.FreeHGlobal(parameterMemory);
			Marshal.FreeHGlobal(variantMemory);
		}
	}

	private static void WriteWaveHeader(
		BinaryWriter writer,
		long dataBytes
	)
	{
		writer.Write("RIFF"u8);
		writer.Write((uint)(36 + dataBytes));
		writer.Write("WAVEfmt "u8);
		writer.Write(16);
		writer.Write((short)1);
		writer.Write((short)Channels);
		writer.Write(SampleRate);
		writer.Write(SampleRate * BytesPerFrame);
		writer.Write((short)BytesPerFrame);
		writer.Write((short)16);
		writer.Write("data"u8);
		writer.Write((uint)dataBytes);
	}

	private static void Check(
		int result
	)
	{
		Marshal.ThrowExceptionForHR(result);
	}

	[DllImport("mmdevapi.dll", ExactSpelling = true)]
	private static extern int ActivateAudioInterfaceAsync(
		[MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
		ref Guid riid,
		IntPtr activationParams,
		IActivateAudioInterfaceCompletionHandler completionHandler,
		out IActivateAudioInterfaceAsyncOperation operation
	);

	private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler
	{
		public readonly ManualResetEventSlim Done = new(false);

		public int ActivateCompleted(
			IActivateAudioInterfaceAsyncOperation operation
		)
		{
			Done.Set();
			return 0;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ActivationParams
	{
		public int Type;

		public int TargetProcessId;

		public int Mode;
	}

	// PROPVARIANT holding a blob
	[StructLayout(LayoutKind.Explicit, Size = 24)]
	private struct BlobVariant
	{
		[FieldOffset(0)]
		public ushort Type;

		[FieldOffset(8)]
		public uint Size;

		[FieldOffset(16)]
		public IntPtr Data;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 2)]
	private struct WaveFormat
	{
		public ushort Tag;

		public ushort Channels;

		public int SamplesPerSecond;

		public int AverageBytesPerSecond;

		public ushort BlockAlign;

		public ushort BitsPerSample;

		public ushort ExtraSize;
	}

	[ComImport]
	[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IActivateAudioInterfaceCompletionHandler
	{
		[PreserveSig]
		int ActivateCompleted(
			IActivateAudioInterfaceAsyncOperation operation
		);
	}

	[ComImport]
	[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IActivateAudioInterfaceAsyncOperation
	{
		void GetActivateResult(
			out int result,
			[MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface
		);
	}

	[ComImport]
	[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioClient
	{
		[PreserveSig]
		int Initialize(
			int shareMode,
			uint streamFlags,
			long bufferDuration,
			long periodicity,
			ref WaveFormat format,
			IntPtr sessionGuid
		);

		[PreserveSig]
		int GetBufferSize(
			out uint frames
		);

		[PreserveSig]
		int GetStreamLatency(
			out long latency
		);

		[PreserveSig]
		int GetCurrentPadding(
			out uint frames
		);

		[PreserveSig]
		int IsFormatSupported(
			int shareMode,
			IntPtr format,
			out IntPtr closestMatch
		);

		[PreserveSig]
		int GetMixFormat(
			out IntPtr format
		);

		[PreserveSig]
		int GetDevicePeriod(
			out long defaultPeriod,
			out long minimumPeriod
		);

		[PreserveSig]
		int Start();

		[PreserveSig]
		int Stop();

		[PreserveSig]
		int Reset();

		[PreserveSig]
		int SetEventHandle(
			IntPtr handle
		);

		[PreserveSig]
		int GetService(
			ref Guid riid,
			[MarshalAs(UnmanagedType.IUnknown)] out object service
		);
	}

	[ComImport]
	[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioCaptureClient
	{
		[PreserveSig]
		int GetBuffer(
			out IntPtr data,
			out uint frames,
			out uint flags,
			out ulong devicePosition,
			out ulong performanceCounter
		);

		[PreserveSig]
		int ReleaseBuffer(
			uint frames
		);

		[PreserveSig]
		int GetNextPacketSize(
			out uint frames
		);
	}
}
