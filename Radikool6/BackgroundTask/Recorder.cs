﻿using System.Diagnostics;
using Radikool6.Classes;
using Radikool6.Entities;

namespace Radikool6.BackgroundTask
{
    public class Recorder
    {
        public string Id { get; set; }
        protected ReserveTask Task { get; set; }
        
        public static Recorder GetRecorder(ReserveTask task)
        {
            Recorder res = null;
            switch (task.Station.Type)
            {
                case Define.Radiko.TypeName:
                    res = new RadikoRecorder(task);
                    break;

            }

            return res;
        }

        protected Recorder(ReserveTask task)
        {
            Task = task;
            Id = task.Id;
        }
    }
}