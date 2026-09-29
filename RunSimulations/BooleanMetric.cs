using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class BooleanMetric : ExtraMetric
    {
        public bool Value { get; set; }
        public BooleanMetric(string name, bool value)
        {
            Tag = name;
            Value = value;
        }
    }
}
