using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTs
{
    internal class MVRegister<T>
    {

        List<(VersionVector,T)> values = new ();

        public MVRegister() { 
        }

        public List<T> Read() { 
            List<T> list = new List<T>();
            foreach (var v in values) 
            { 
            }
            return list;
        }

        public MVRegister<T> Write(T value) {
            return new MVRegister<T>();
        }

        public void Merge() { 
        
        }
    }
}
