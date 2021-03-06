﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Windows.Forms;
using GLib.GeneralModel;
using GLib.Extension;

using System.Runtime.InteropServices;
using System.Threading;

using System.IO;
using SS.Media;
using SS.Media.Wave.Wave;


namespace SS.ClientBase.Codec
{


    public class MicCapturer : IDisposable
    {
        private bool _isworking = false;
        private int _mic = 0;
        private int _channels = 0;
        private int _samples = 0;
        private int _bufferSize = 0;
        private WaveIn _waveIn = null;//音频输入
        private Action<byte[]> _callBack;
        public MicCapturer(int mic, int channels, int samples, int bufferSize, Action<byte[]> callback)
        {
            _mic = mic;
            _bufferSize = bufferSize;
            _channels = channels;
            _samples = samples;
            _callBack = callback;
            try
            {
                _waveIn = new WaveIn(WaveIn.Devices[mic], _samples, 16, _channels, bufferSize);
                _waveIn.BufferFull += new BufferFullHandler(WaveIn_BufferFull);
            }
            catch (Exception e)
            {

            }
        }

        public void Start()
        {

            if (_isworking)
                return;

            _isworking = true;
            if (_waveIn != null)
                _waveIn.Start();
        }

        public void Stop()
        {
            if (!_isworking)
                return;
            _isworking = false;
            if (_waveIn != null)
                _waveIn.Stop();

        }

        private void WaveIn_BufferFull(byte[] buffer)
        {
            int buf_size = _bufferSize;
            if (buffer.Length / buf_size > 1)
            {
                for (int i = 0; i < buffer.Length / buf_size; i++)
                {
                    var tbuf = new byte[buf_size];
                    Array.Copy(buffer, i * buf_size, tbuf, 0, buf_size);
                    _callBack(buffer);
                }
            }
            else
            {
                _callBack(buffer);
            }
        }

        public void Dispose()
        {

            try
            {
                Stop();
                _waveIn.Dispose();
            }
            catch (Exception e)
            {
                throw;
            }
        }

    }

    public class MicEncoder : IDisposable
    {
        private bool _isworking = false;
        private MicCapturer _capturer = null;
        private Speex _speex = null;//音频编码器
        private FaacImp _faacImp = null;
        private int _audioFrameIndex = 0;
        private int _frequency = 0;
        private int _channels = 0;
        private Action<MediaFrame> _callBack;
        private AudioEncodeCfg _audioCfg = null;
        private bool _isFirstKeyFrame = true;
        public event EventHandler<EventArgsEx<Exception>> Error;
        public MicEncoder(AudioEncodeCfg audioCfg, Action<MediaFrame> callback)
        {
            _audioCfg = audioCfg;
            _channels = audioCfg.channel;
            _frequency = audioCfg.frequency;
            _capturer = new MicCapturer(audioCfg.micId, _channels, _frequency, audioCfg.samples, MicCapturer_CallBack);
            if (audioCfg.encodeName.EqIgnoreCase("SPEX"))
                _speex = new Speex(4);
            else if (audioCfg.encodeName.EqIgnoreCase("AAC_"))
            {
                if (audioCfg.Params.ContainsKey("UseLastFaacImp") && FaacImp.LastFaacImp != null)
                {
                    _faacImp = FaacImp.LastFaacImp;
                    _faacImp.Encode(new byte[2048]);
                    _faacImp.Encode(new byte[2048]);
                    _faacImp.Encode(new byte[2048]);
                    _faacImp.Encode(new byte[2048]);
                    _faacImp.Encode(new byte[2048]);
                }
                else
                    _faacImp = new FaacImp(_channels, _frequency, audioCfg.bitrate);
            }

            _callBack = callback;

        }
        System.IO.BinaryWriter bw;
        private void MicCapturer_CallBack(byte[] buffer)
        {

            var buf = Enc_AAC(buffer);

            if (buf == null)
                return;
            if (buf.Length == 0)
                return;
            //生成媒体帧
            var mf = new SS.Media.MediaFrame()
            {
                nFrequency = _frequency,
                nSamples = (short)_audioCfg.samples,
                nIsKeyFrame = (byte)((_audioFrameIndex++ % 50) == 0 ? 1 : 0),
                nEncoder = _audioCfg.encoder,
                MediaFrameVersion = 0,
                nChannel = _channels,
                nAudioFormat = 2,
                nIsAudio = 1,
                nTimetick = Environment.TickCount,
                nEx = 1,
                Data = buf,
                nSize = buf.Length,
            };

            mf.MediaFrameVersion = (byte)(mf.nIsKeyFrame == 1 ? 1 : 0);
            mf.nEx = (byte)(mf.nIsKeyFrame == 1 ? 0 : 1);

            if (_isFirstKeyFrame)
            {
                _isFirstKeyFrame = false;
                var resetCodecMediaFrame = CreateResetCodecMediaFrame(mf);
                if (_callBack != null)
                    _callBack(resetCodecMediaFrame);
            }

            if (_callBack != null)
                _callBack(mf);
            if (bw == null)
                bw = new BinaryWriter(new System.IO.FileStream(@"D:\aac5.aac", System.IO.FileMode.Create));
            byte[] bufs = mf.GetBytes();
            bw.Write(bufs.Length);
            bw.Write(bufs);
        }

        protected MediaFrame CreateResetCodecMediaFrame(MediaFrame mf)
        {
            var infoMediaFrame = new MediaFrame()
            {

                nFrequency = mf.nFrequency,
                nSamples = mf.nSamples,
                nIsKeyFrame = mf.nIsKeyFrame,
                nEncoder = mf.nEncoder,
                MediaFrameVersion = mf.MediaFrameVersion,
                nChannel = mf.nChannel,
                nAudioFormat = mf.nAudioFormat,
                nIsAudio = mf.nIsAudio,
                nTimetick = mf.nTimetick,
                nEx = mf.nEx,

                Data = new byte[0],
                nSize = 0,
            };

            var resetCodecMediaFrame = MediaFrame.CreateCommandMediaFrame(true, MediaFrameCommandType.ResetCodec, infoMediaFrame.GetBytes());
            return resetCodecMediaFrame;
        }

        private byte[] Enc_SPEX(byte[] buffer)
        {
            return _speex.Encode(buffer);
        }

        private byte[] Enc_AAC(byte[] buffer)
        {
            return _faacImp.Encode(buffer);
        }

        public void Start()
        {
            if (_isworking)
                return;
            _isworking = true;
            if (_capturer != null)
                _capturer.Start();
        }

        public void Stop()
        {
            if (!_isworking)
                return;
            _isworking = false;
            if (_capturer != null)
                _capturer.Stop();

        }

        public void Dispose()
        {
            try
            {
                if (_capturer != null)
                    _capturer.Dispose();
                if (_speex != null)
                    _speex.Dispose();
                if (_faacImp != null)
                    _faacImp.Dispose();
            }
            catch (Exception e)
            {
            }
        }
    }
}
