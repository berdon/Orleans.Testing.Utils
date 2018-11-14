using System;
using Orleans;

namespace SharedOrleansUtils
{
    [Serializable]
    public class GrainState<T> : IGrainState
    {
        public T State;

        object IGrainState.State
        {
            get
            {
                return State;

            }
            set
            {
                State = (T)value;
            }
        }

        /// <inheritdoc />
        public string ETag { get; set; }

        /// <summary>Initializes a new instance of <see cref="GrainState{T}"/>.</summary>
        public GrainState()
        {
        }

        /// <summary>Initializes a new instance of <see cref="GrainState{T}"/>.</summary>
        /// <param name="state"> The initial value of the state.</param>
        public GrainState(T state) : this(state, null)
        {
        }

        /// <summary>Initializes a new instance of <see cref="GrainState{T}"/>.</summary>
        /// <param name="state">The initial value of the state.</param>
        /// <param name="eTag">The initial e-tag value that allows optimistic concurrency checks at the storage provider level.</param>
        public GrainState(T state, string eTag)
        {
            State = state;
            ETag = eTag;
        }
    }
}
