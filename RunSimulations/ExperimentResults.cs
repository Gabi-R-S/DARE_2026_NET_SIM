using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class ExperimentResults
    {
        public string Tag { get; private set; }
        public bool CompletedSuccessfully { get; private set; }
        public int TotalMessagesSent { get; private set; }
        public float CompletionTimeSeconds { get; private set; }

        List<ExtraMetric> Extras { get; set; }
        public ExperimentResults(string tag, bool completedSuccessfully, int totalMessagesSent, float completionTimeSeconds, List<ExtraMetric> extras) {
            Tag = tag;
            TotalMessagesSent = totalMessagesSent;
            CompletionTimeSeconds = completionTimeSeconds;
            Extras = extras;
            CompletedSuccessfully = completedSuccessfully;
        }
    }
}
