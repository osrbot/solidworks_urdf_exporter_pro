using System;

namespace SW2URDF.UI
{
    internal sealed class TreeSelectionUpdateGuard
    {
        private int suppressionDepth;

        public bool IsSuppressed
        {
            get { return suppressionDepth > 0; }
        }

        public IDisposable Suppress()
        {
            suppressionDepth++;
            return new SuppressionScope(this);
        }

        private void Release()
        {
            if (suppressionDepth == 0)
            {
                throw new InvalidOperationException("Tree selection suppression is not active.");
            }

            suppressionDepth--;
        }

        private sealed class SuppressionScope : IDisposable
        {
            private TreeSelectionUpdateGuard owner;

            public SuppressionScope(TreeSelectionUpdateGuard owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }

                owner.Release();
                owner = null;
            }
        }
    }
}
