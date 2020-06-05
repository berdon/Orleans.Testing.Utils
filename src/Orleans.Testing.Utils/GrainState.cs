using System;
using Orleans;

namespace Orleans.Testing.Utils
{
    [Serializable]
    public class GrainState<T> : IGrainState where T : new()
    {
        public T State;

        public Type Type => typeof(T);

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
        public GrainState() : this(new T(), null)
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
