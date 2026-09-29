using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class ExperimentResults
    {
        public string Tag { get; private set; }
        public int TotalMessagesSent { get; private set; }
        
        public List<ExtraMetric> Extras { get; set; }
        public ExperimentResults(string tag, int totalMessagesSent, List<ExtraMetric> extras) {
            Tag = tag;
            TotalMessagesSent = totalMessagesSent;
            Extras = extras;
        }
    }
}
