using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class CounterMetric:ExtraMetric
    {
        int Counter{ get; set; }
        public CounterMetric(string name, int counter) {
            Tag = name;
            Counter = counter;
        }
    }
}
