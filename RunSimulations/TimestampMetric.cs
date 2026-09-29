using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class TimestampMetric : ExtraMetric
    {
        public DateTime TimesStamp { get; set; }
        public TimestampMetric(string name, DateTime timesStamp)
        {
            Tag = name;
            TimesStamp=timesStamp;
        }
    }
}
