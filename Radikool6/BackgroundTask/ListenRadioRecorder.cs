﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NLog;
using Radikool6.Classes;
using Radikool6.Entities;
using Radikool6.Models;
using Radikool6.Radio;

namespace Radikool6.BackgroundTask
{
    public class ListenRadioRecorder : Recorder, IRecorder
    {
        private string _token;
        private Process _ffmpeg;
        private string _filename;
        private Entities.Program _program = new Entities.Program();
        private bool _isComplete = false;
        private string _ffmpegOutput;
        private int _duration;
        public Action<int> ChangeProgress { get; set; } = (progress) => { };

        public ListenRadioRecorder(Config config, ReserveTask task = null) : base(config, task)
        {
        }


        /// <summary>
        /// 録音開始
        /// </summary>
        /// <returns></returns>
        public async Task Start()
        {
            try
            {
                Status = RecorderStatus.Working;
                using (var con = new SqliteConnection($"Data Source={Define.File.DbFile}"))
                {
                    var pModel = new ProgramModel(con);

                    // 番組判定用の時刻

                    _program = pModel.Search(new ProgramSearchCondition()
                    {
                        StationId = Task.Station.Id,
                        From = Task.ProgramStart,
                        To = Task.ProgramEnd
                    }).FirstOrDefault();
                    _program.Station = Task.Station;
                }

                Directory.CreateDirectory(Path.Combine("wwwroot", "records"));
                _filename = Path.GetFullPath(Path.Combine("wwwroot", "records", $"{Guid.NewGuid().ToString()}.m4a"));
                StartTime = DateTime.Now;
                var arg = Define.ListenRadio.RealTimeFfmpegArgs.Replace("[M3U8]", Task.Station.MediaUrl)
                    .Replace("[TIME]",
                        (Task.End.AddSeconds(Define.Radiko.EndSec) - DateTime.Now).ToString(@"hh\:mm\:ss"))
                    .Replace("[FILE]", _filename);
                arg = Replace(arg, _program);
                CreateProcess(arg);

                _ffmpeg.Start();
                _ffmpeg.BeginOutputReadLine();
                _ffmpeg.BeginErrorReadLine();


            }
            catch (Exception ex)
            {
                var a = ex.Message;
            }
        }

        /// <summary>
        /// ステータス取得
        /// </summary>
        /// <returns></returns>
        public ReserveTask GetStatus()
        {
            var statusText = "";
            
            // ffmpegの状態確認
            if (_ffmpeg.HasExited)
            {
                if (Status == RecorderStatus.Stopping)
                {
                    Status = RecorderStatus.Stopped;
                }
                else
                {
                    Status = DateTime.Now < Task.End ? RecorderStatus.Error : RecorderStatus.End;
                }
            }

            switch (Status)
            {
                case RecorderStatus.Stopped:
                    statusText = "停止中";
                    break;

                case RecorderStatus.Stopping:
                    statusText = "停止処理中";
                    break;
                
                case RecorderStatus.Error:
                    statusText = "エラー";
                    break;
                
                case RecorderStatus.End:
                    statusText = "完了";
                    break;
                
                case RecorderStatus.Working:
                    statusText = "正常";
                    break;
            }

            return new ReserveTask(){ Id = Id, Start = StartTime, End = Task.End, Status = statusText};
        }

        /// <summary>
        /// 停止／再開
        /// </summary>
        public void StopRestart()
        {
            if (Task.Reserve.IsTimeFree) return;
            if (Status == RecorderStatus.Stopped)
            {
                // 再開
                Start();
            }
            else if(Status == RecorderStatus.Working)
            {
                // 停止
                Stop();
            }
        }

        /// <summary>
        /// ffmpegのプロセス作成
        /// </summary>
        /// <param name="arg"></param>
        private void CreateProcess(string arg)
        {
            _ffmpeg = new Process
            {
                StartInfo =
                {
                    FileName = "ffmpeg",
                    Arguments = arg,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                }
            };

            
            _ffmpeg.OutputDataReceived += process_OutputDataReceived;
            _ffmpeg.ErrorDataReceived += process_OutputDataReceived;

            
            Global.Logger.Info($"ffmpeg起動:{arg}");
        }



        

        /// <summary>
        /// 停止
        /// </summary>
        private void Stop()
        {
            Status = RecorderStatus.Stopping;
            _ffmpeg.Kill();            
        }


        private void process_Exited(object sender, System.EventArgs e)
        {
            using (var con = new SqliteConnection($"Data Source={Define.File.DbFile}"))
            {
                con.Open();
                var lModel = new LibraryModel(con);
                lModel.Update(new Library() { Id = Guid.NewGuid().ToString(), FileName = Replace(Config.FileName, _program), Path = _filename, Program = _program });

            }
        }

        private void process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
            {
                if (_isComplete) return;
                _isComplete = true;
                process_Exited(sender, e);
            }
            else
            {
                _ffmpegOutput = e.Data;
            }

            int progress = 0;
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                var time = e.Data.Split(" ").FirstOrDefault(t => t.Contains("time=")) ?? "";
                var m = Regex.Match(time, @"([0-9]+):([0-9]+):([0-9.]+)");
                if (m.Success)
                {
                    var current = Convert.ToInt32(m.Groups[1].Value) * 3600 + Convert.ToInt32(m.Groups[2].Value) * 60 + Convert.ToDouble(m.Groups[3].Value);
                    progress = ((int) (current / _duration * 100));
                }
            }

            if (e.Data == null || progress > 0)
            {
                ChangeProgress(e.Data != null ? progress : -1);
            }            
            
        }


    }
}