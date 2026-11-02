using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace SERAAC.Core
{
    // Plays a video file (MP4 / H.264 + AAC, anything Windows can play) into a texture, with its sound.
    // MonoGame DesktopGL has no video support, so this decodes with Windows Media Foundation's Source Reader:
    // a background thread reads frames (NV12, converted to RGBA here) and PCM audio; the game thread uploads the
    // frame that is due and feeds the audio to a DynamicSoundEffectInstance. Both start together, so they stay in sync.
    // On anything but Windows (or if the file can't be opened) Failed is true and callers just skip the video.
    public sealed unsafe class VideoPlayer : IDisposable
    {
        public Texture2D Texture { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool Failed { get; private set; }
        public bool Finished { get; private set; }
        public double Time => _clock.Elapsed.TotalSeconds;
        public float Volume = 1f;

        private readonly GraphicsDevice _device;
        private IntPtr _reader;
        private Thread _thread;
        private volatile bool _stop, _videoEnded, _audioEnded;
        private readonly Stopwatch _clock = new();
        private bool _started;

        private readonly object _lock = new();
        private readonly Queue<(double time, byte[] pixels)> _frames = new();
        private readonly Queue<byte[]> _audio = new();
        private readonly Stack<byte[]> _spareFrames = new();
        private double _audioQueuedSeconds;   // decoded but not yet handed to the sound instance
        private const int MaxFrames = 4;
        private const double AudioAhead = 1.0;

        private DynamicSoundEffectInstance _sound;
        private int _sampleRate, _channels;
        private bool _hasAudio;

        public VideoPlayer(GraphicsDevice device, string path)
        {
            _device = device;
            try { Open(path); }
            catch (Exception e)
            {
                Console.WriteLine($"[Video] can't play {path}: {e.Message}");
                Failed = true;
                Release(ref _reader);
            }
        }

        // ------------------------------------------------------------------ game thread

        // Call once per frame. Starts the clock as soon as the first frame (and some sound) is ready.
        public void Update()
        {
            if (Failed || Finished) return;
            lock (_lock)
            {
                if (!_started)
                {
                    bool audioReady = !_hasAudio || _audioEnded || _audioQueuedSeconds >= 0.25;
                    if (_frames.Count == 0 && !_videoEnded || !audioReady) return;
                    _started = true;
                    _clock.Start();
                    _sound?.Play();
                }

                // Audio: keep a few buffers queued in the sound instance.
                while (_sound != null && _audio.Count > 0 && _sound.PendingBufferCount < 4)
                {
                    var buf = _audio.Dequeue();
                    _audioQueuedSeconds -= buf.Length / (2.0 * _channels * _sampleRate);
                    _sound.SubmitBuffer(buf);
                }

                // Video: show the newest frame that is due.
                double now = Time;
                byte[] show = null;
                while (_frames.Count > 0 && _frames.Peek().time <= now)
                {
                    if (show != null) _spareFrames.Push(show);
                    show = _frames.Dequeue().pixels;
                }
                if (show != null)
                {
                    Texture ??= new Texture2D(_device, Width, Height, false, SurfaceFormat.Color);
                    Texture.SetData(show);
                    _spareFrames.Push(show);
                }

                // Nothing left to see or hear (a black, silent tail): end here rather than wait it out.
                if (now > 3 && now > Math.Max(_lastPicture, _hasAudio ? _lastSound : 0) + TailSeconds)
                    Finished = true;
                if (_videoEnded && _frames.Count == 0 && (!_hasAudio || _sound == null || _audio.Count == 0 && _sound.PendingBufferCount == 0 || _audioEnded && now > _endTime + 0.3))
                    Finished = true;
            }
        }

        // Draws the current frame letterboxed into `area`.
        public void Draw(SpriteBatch sb, Rectangle area, Color tint)
        {
            if (Texture == null) return;
            float s = Math.Min(area.Width / (float)Width, area.Height / (float)Height);
            int w = (int)(Width * s), h = (int)(Height * s);
            sb.Draw(Texture, new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, h), tint);
        }

        public void Dispose()
        {
            _stop = true;
            _thread?.Join(1000);
            lock (_lock) { _frames.Clear(); _audio.Clear(); }
            try { _sound?.Stop(); _sound?.Dispose(); } catch { }
            Texture?.Dispose();
            Release(ref _reader);
            if (_mfStarted) { MFShutdown(); _mfStarted = false; }
        }

        // ------------------------------------------------------------------ decoding thread

        private double _endTime;

        // Where the picture last showed something and the sound was last heard (decoder clock, seconds).
        // A file that ends in a long black, silent tail is treated as over once that tail is TailSeconds long.
        private volatile float _lastPicture, _lastSound;
        private const double TailSeconds = 2.0;

        private static bool IsBlack(byte[] rgba)
        {
            long sum = 0;
            int n = 0;
            for (int i = 0; i < rgba.Length; i += 4 * 61, n++) sum += rgba[i] + rgba[i + 1] + rgba[i + 2];
            return n > 0 && sum / (3.0 * n) < 3;
        }

        private static bool IsSilent(byte[] pcm16)
        {
            double acc = 0;
            int n = 0;
            for (int i = 0; i + 1 < pcm16.Length; i += 8, n++) { short v = (short)(pcm16[i] | pcm16[i + 1] << 8); acc += v * v; }
            return n == 0 || Math.Sqrt(acc / n) < 60;
        }

        private void DecodeLoop()
        {
            try
            {
                while (!_stop && !(_videoEnded && (_audioEnded || !_hasAudio)))
                {
                    bool worked = false;
                    int frames;
                    double audioAhead;
                    lock (_lock) { frames = _frames.Count; audioAhead = _audioQueuedSeconds; }
                    if (!_videoEnded && frames < MaxFrames) { ReadVideo(); worked = true; }
                    if (_hasAudio && !_audioEnded && audioAhead < AudioAhead) { ReadAudio(); worked = true; }
                    if (!worked) Thread.Sleep(3);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Video] decoding stopped: {e.Message}");
                _videoEnded = _audioEnded = true;
            }
        }

        private void ReadVideo()
        {
            var sample = ReadSample(FirstVideoStream, out uint flags, out long stamp);
            if ((flags & EndOfStream) != 0) _videoEnded = true;
            if (sample == IntPtr.Zero) return;
            try
            {
                byte[] pixels;
                lock (_lock) pixels = _spareFrames.Count > 0 ? _spareFrames.Pop() : null;
                pixels ??= new byte[Width * Height * 4];
                CopyFrame(sample, pixels);
                double t = stamp / 1e7;
                if (!IsBlack(pixels)) _lastPicture = (float)Math.Max(_lastPicture, t);
                lock (_lock)
                {
                    _frames.Enqueue((t, pixels));
                    _endTime = Math.Max(_endTime, t);
                }
            }
            finally { Release(ref sample); }
        }

        private void ReadAudio()
        {
            var sample = ReadSample(FirstAudioStream, out uint flags, out long stamp);
            if ((flags & EndOfStream) != 0) _audioEnded = true;
            if (sample == IntPtr.Zero) return;
            try
            {
                var bytes = CopyBuffer(sample);
                if (bytes.Length < 4) return;
                if (!IsSilent(bytes)) _lastSound = (float)Math.Max(_lastSound, stamp / 1e7);
                lock (_lock)
                {
                    _audio.Enqueue(bytes);
                    _audioQueuedSeconds += bytes.Length / (2.0 * _channels * _sampleRate);
                }
            }
            finally { Release(ref sample); }
        }

        // The decoder hands out NV12 (a full-size Y plane, then a half-size plane of interleaved U,V); converted
        // here to R,G,B,A with the HD (BT.709, limited range) matrix, rows split across the CPU cores.
        private void CopyFrame(IntPtr sample, byte[] dest)
        {
            IntPtr buffer = IntPtr.Zero;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(sample, 41))(sample, &buffer), "ConvertToContiguousBuffer");   // IMFSample::ConvertToContiguousBuffer
            try
            {
                byte* data; uint max, cur;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &data, &max, &cur), "Lock");
                try
                {
                    int stride = _stride > 0 ? _stride : Width;
                    // The decoded picture can be taller than what is shown (1088 rows for 1080p): the U,V plane starts after all of it.
                    int planeRows = Math.Max(Height, (int)(cur * 2 / 3 / (uint)stride));
                    byte* yPlane = data;
                    byte* uvPlane = data + (long)stride * planeRows;
                    int w = Width;
                    fixed (byte* dst0 = dest)
                    {
                        byte* dst = dst0;
                        System.Threading.Tasks.Parallel.For(0, Height / 2, pair =>
                        {
                            byte* uvRow = uvPlane + (long)pair * stride;
                            for (int dy = 0; dy < 2; dy++)
                            {
                                int y = pair * 2 + dy;
                                byte* yRow = yPlane + (long)y * stride;
                                uint* d = (uint*)(dst + (long)y * w * 4);
                                for (int x = 0; x < w; x++)
                                {
                                    int c = 298 * (yRow[x] - 16);
                                    int u = uvRow[x & ~1] - 128, v = uvRow[x | 1] - 128;
                                    int r = (c + 459 * v + 128) >> 8;
                                    int g = (c - 55 * u - 136 * v + 128) >> 8;
                                    int b = (c + 541 * u + 128) >> 8;
                                    r = r < 0 ? 0 : r > 255 ? 255 : r;
                                    g = g < 0 ? 0 : g > 255 ? 255 : g;
                                    b = b < 0 ? 0 : b > 255 ? 255 : b;
                                    d[x] = 0xFF000000u | (uint)(b << 16) | (uint)(g << 8) | (uint)r;
                                }
                            }
                        });
                    }
                }
                finally { ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(buffer, 4))(buffer); }   // Unlock
            }
            finally { Release(ref buffer); }
        }

        private static byte[] CopyBuffer(IntPtr sample)
        {
            IntPtr buffer = IntPtr.Zero;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(sample, 41))(sample, &buffer), "ConvertToContiguousBuffer");
            try
            {
                byte* data; uint max, cur;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &data, &max, &cur), "Lock");
                try
                {
                    var bytes = new byte[cur & ~3u];
                    Marshal.Copy((IntPtr)data, bytes, 0, bytes.Length);
                    return bytes;
                }
                finally { ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(buffer, 4))(buffer); }
            }
            finally { Release(ref buffer); }
        }

        private IntPtr ReadSample(uint stream, out uint flags, out long stamp)
        {
            uint actual, f; long ts; IntPtr sample = IntPtr.Zero;
            // IMFSourceReader::ReadSample(stream, controlFlags, *actualStream, *flags, *timestamp, **sample)
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)Slot(_reader, 9))(_reader, stream, 0, &actual, &f, &ts, &sample), "ReadSample");
            flags = f; stamp = ts;
            return sample;
        }

        // ------------------------------------------------------------------ setup

        private bool _mfStarted;
        private int _stride;

        private void Open(string path)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("video needs Windows Media Foundation");
            if (!System.IO.File.Exists(path)) throw new System.IO.FileNotFoundException(path);
            Check(MFStartup(0x00020070, 0), "MFStartup");
            _mfStarted = true;

            IntPtr reader;
            Check(MFCreateSourceReaderFromURL(path, IntPtr.Zero, &reader), "MFCreateSourceReaderFromURL");
            _reader = reader;

            // Video out as NV12: what H.264 decoders produce natively, so nothing slow sits in between
            // (Media Foundation's own RGB conversion can't keep up with 1080p).
            SetOutputType(FirstVideoStream, MFMediaType_Video, MFVideoFormat_NV12);
            IntPtr type = IntPtr.Zero;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(_reader, 6))(_reader, FirstVideoStream, &type), "GetCurrentMediaType");
            try
            {
                ulong size;
                var key = MF_MT_FRAME_SIZE;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)Slot(type, 8))(type, &key, &size), "GetUINT64");   // IMFAttributes::GetUINT64
                Width = (int)(size >> 32);
                Height = (int)(size & 0xFFFFFFFF);
                uint stride;
                key = MF_MT_DEFAULT_STRIDE;
                if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Slot(type, 7))(type, &key, &stride) >= 0) _stride = (int)stride;   // GetUINT32
                // The part of the picture meant to be shown (e.g. 1080 of 1088 decoded rows). MFVideoArea: x, y offsets, then cx, cy.
                byte* area = stackalloc byte[16];
                uint areaSize;
                key = MF_MT_MINIMUM_DISPLAY_APERTURE;
                if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, byte*, uint, uint*, int>)Slot(type, 15))(type, &key, area, 16, &areaSize) >= 0 && areaSize >= 16)   // GetBlob
                {
                    int cx = *(int*)(area + 8), cy = *(int*)(area + 12);
                    if (cx > 0 && cx <= Width) Width = cx;
                    if (cy > 0 && cy <= Height) Height = cy;
                }
                Width &= ~1;
                Height &= ~1;
            }
            finally { Release(ref type); }

            // Audio out as 16-bit PCM, if there is any and it's mono or stereo.
            try
            {
                SetOutputType(FirstAudioStream, MFMediaType_Audio, MFAudioFormat_PCM, bits: 16);
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(_reader, 6))(_reader, FirstAudioStream, &type), "GetCurrentMediaType");
                try
                {
                    uint rate, ch;
                    var key = MF_MT_AUDIO_SAMPLES_PER_SECOND;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Slot(type, 7))(type, &key, &rate), "GetUINT32");
                    key = MF_MT_AUDIO_NUM_CHANNELS;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Slot(type, 7))(type, &key, &ch), "GetUINT32");
                    _sampleRate = (int)rate;
                    _channels = (int)ch;
                }
                finally { Release(ref type); }
                if (_channels is 1 or 2)
                {
                    _sound = new DynamicSoundEffectInstance(_sampleRate, _channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
                    _sound.Volume = MathHelper.Clamp(Volume, 0, 1);
                    _hasAudio = true;
                }
            }
            catch (Exception e) { Console.WriteLine($"[Video] no sound: {e.Message}"); }
            if (!_hasAudio) ((delegate* unmanaged[Stdcall]<IntPtr, uint, int, int>)Slot(_reader, 4))(_reader, FirstAudioStream, 0);   // SetStreamSelection(audio, false)

            Console.WriteLine($"[Video] {System.IO.Path.GetFileName(path)}: {Width}x{Height}" + (_hasAudio ? $", sound {_sampleRate} Hz x{_channels}" : ", no sound"));
            _thread = new Thread(DecodeLoop) { IsBackground = true, Name = "Video decode" };
            _thread.Start();
        }

        private void SetOutputType(uint stream, Guid major, Guid subtype, uint bits = 0)
        {
            IntPtr type = IntPtr.Zero;
            Check(MFCreateMediaType(&type), "MFCreateMediaType");
            try
            {
                var key = MF_MT_MAJOR_TYPE;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Slot(type, 24))(type, &key, &major), "SetGUID");   // IMFAttributes::SetGUID
                key = MF_MT_SUBTYPE;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Slot(type, 24))(type, &key, &subtype), "SetGUID");
                if (bits != 0)
                {
                    key = MF_MT_AUDIO_BITS_PER_SAMPLE;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Slot(type, 21))(type, &key, bits), "SetUINT32");
                }
                // IMFSourceReader::SetCurrentMediaType(stream, reserved, type)
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Slot(_reader, 7))(_reader, stream, IntPtr.Zero, type), "SetCurrentMediaType");
            }
            finally { Release(ref type); }
        }

        // ------------------------------------------------------------------ Media Foundation interop
        // COM calls go straight through the vtable: slot numbers are IUnknown (0-2) + the interface's methods in order.

        private const uint FirstVideoStream = 0xFFFFFFFC, FirstAudioStream = 0xFFFFFFFD;
        private const uint EndOfStream = 0x2;

        private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
        private static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        private static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        private static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        private static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
        private static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00aa00389b71");
        private static readonly Guid MFVideoFormat_NV12 = new("3231564e-0000-0010-8000-00aa00389b71");
        private static readonly Guid MF_MT_MINIMUM_DISPLAY_APERTURE = new("d7388766-18fe-48c6-a177-ee894867c8c4");
        private static readonly Guid MFAudioFormat_PCM = new("00000001-0000-0010-8000-00aa00389b71");

        [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] private static extern int MFShutdown();
        [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(IntPtr* attributes, uint initialSize);
        [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(IntPtr* type);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, IntPtr* reader);

        private static void* Slot(IntPtr obj, int slot) => (*(void***)obj)[slot];

        private static void Release(ref IntPtr obj)
        {
            if (obj == IntPtr.Zero) return;
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 2))(obj);
            obj = IntPtr.Zero;
        }

        private static void Check(int hr, string what)
        {
            if (hr < 0) throw new InvalidOperationException($"{what} failed (0x{hr:X8})");
        }
    }
}
