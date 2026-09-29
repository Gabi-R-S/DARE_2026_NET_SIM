using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Behaviours
{
    public abstract class Behaviour
    {

        public abstract Task Run();

        public abstract void Stop();

        event Action? BehaviourEnded;

        protected void CallBehaviourEnded() {
            BehaviourEnded?.Invoke();
        }

        public void AddBehaviourEndedHandler(Action handler) {
            BehaviourEnded += handler;
        }

        public void RemoveBehaviourEndedHandler(Action handler)
        {
            BehaviourEnded -= handler;
        }
    }
}
