using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class FloatMetric : ExtraMetric
    {
        public float Value{ get; set; }
        public FloatMetric(string name, float value)
        {
            Tag = name;
            Value = value;
        }
    }
}
