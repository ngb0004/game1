using System;
using System.Collections.Generic;

namespace RR.Sim.Core
{
    /// Synchronous publish/subscribe. Subscribers run inside the sim step, so
    /// they must not call back into the sim; the presentation layer buffers.
    public sealed class EventBus
    {
        readonly List<Action<SimEvent>> _subscribers = new List<Action<SimEvent>>();

        public void Subscribe(Action<SimEvent> handler) => _subscribers.Add(handler);

        public void Unsubscribe(Action<SimEvent> handler) => _subscribers.Remove(handler);

        public void Publish(SimEvent e)
        {
            for (int i = 0; i < _subscribers.Count; i++) _subscribers[i](e);
        }
    }
}
