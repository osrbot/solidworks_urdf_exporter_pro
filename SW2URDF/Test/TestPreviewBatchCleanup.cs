using Moq;
using SolidWorks.Interop.sldworks;
using SW2URDF.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace SW2URDF.Test
{
    public class TestPreviewBatchCleanup
    {
        [Theory]
        [InlineData(true, true, 1)]
        [InlineData(true, false, 1)]
        [InlineData(false, true, 1)]
        [InlineData(false, false, 0)]
        public void BatchClearsAllVisibleBodiesBeforeOneRedraw(
            bool inertiaVisible, bool collisionVisible, int expectedRedraws)
        {
            var events = new List<string>();
            var model = new Mock<ModelDoc2>(MockBehavior.Strict);
            using (var inertia = new InertiaPreview(null, model.Object))
            using (var collision = new CollisionPreview(null, model.Object, null))
            {
                model.Setup(item => item.GraphicsRedraw2()).Callback(() =>
                {
                    Assert.False(inertia.IsVisible);
                    Assert.False(collision.IsVisible);
                    events.Add("redraw");
                });
                var inertiaBody = AddBody(inertia, model, events, "inertia", inertiaVisible);
                var collisionBody = AddBody(collision, model, events, "collision", collisionVisible);

                ClearBatch(inertia, collision, model);
                ClearBatch(inertia, collision, model);

                var expected = new List<string>();
                if (inertiaVisible) expected.Add("inertia");
                if (collisionVisible) expected.Add("collision");
                if (expectedRedraws > 0) expected.Add("redraw");
                Assert.Equal(expected, events);
                inertiaBody.Verify(item => item.Hide(model.Object),
                    Times.Exactly(inertiaVisible ? 1 : 0));
                collisionBody.Verify(item => item.Hide(model.Object),
                    Times.Exactly(collisionVisible ? 1 : 0));
            }
            model.Verify(item => item.GraphicsRedraw2(), Times.Exactly(expectedRedraws));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BodyHideFailureDoesNotPreventOtherBodiesOrPreviewCleanup(bool failInertia)
        {
            var events = new List<string>();
            var model = new Mock<ModelDoc2>(MockBehavior.Strict);
            model.Setup(item => item.GraphicsRedraw2()).Callback(() => events.Add("redraw"));
            using (var inertia = new InertiaPreview(null, model.Object))
            using (var collision = new CollisionPreview(null, model.Object, null))
            {
                var first = AddBody(inertia, model, events, "inertia", true);
                AddBody(inertia, model, events, "inertia-next", true);
                var second = AddBody(collision, model, events, "collision", true);
                AddBody(collision, model, events, "collision-next", true);
                (failInertia ? first : second).Setup(item => item.Hide(model.Object))
                    .Callback(() => events.Add(failInertia ? "inertia" : "collision"))
                    .Throws(new InvalidOperationException("Body already discarded"));

                ClearBatch(inertia, collision, model);

                Assert.False(inertia.IsVisible);
                Assert.False(collision.IsVisible);
                Assert.Equal(new[] { "inertia", "inertia-next", "collision", "collision-next", "redraw" }, events);
            }
            model.Verify(item => item.GraphicsRedraw2(), Times.Once);
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, true, false)]
        public void CleanupExceptionsStillAttemptBothCallbacksAndConditionalRedraw(
            bool failInertia, bool failCollision, bool needsRedraw)
        {
            var events = new List<string>();
            var inertiaFailure = new InvalidOperationException("Inertia cleanup failed");
            var collisionFailure = new InvalidOperationException("Collision cleanup failed");
            var model = new Mock<ModelDoc2>(MockBehavior.Strict);
            model.Setup(item => item.GraphicsRedraw2()).Callback(() => events.Add("redraw"));

            var failure = Assert.Throws<InvalidOperationException>(() =>
                AssemblyExportForm.ClearPreviews(needsRedraw,
                    () =>
                    {
                        events.Add("inertia");
                        if (failInertia) throw inertiaFailure;
                    },
                    () =>
                    {
                        events.Add("collision");
                        if (failCollision) throw collisionFailure;
                    },
                    () => model.Object.GraphicsRedraw2()));

            Assert.Same(failCollision ? collisionFailure : inertiaFailure, failure);
            Assert.Equal(needsRedraw
                ? new[] { "inertia", "collision", "redraw" }
                : new[] { "inertia", "collision" }, events);
            model.Verify(item => item.GraphicsRedraw2(), Times.Exactly(needsRedraw ? 1 : 0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PublicHideAndDisposeStillRedrawEachPreviewIndependently(bool dispose)
        {
            var events = new List<string>();
            var model = new Mock<ModelDoc2>(MockBehavior.Strict);
            model.Setup(item => item.GraphicsRedraw2()).Callback(() => events.Add("redraw"));
            using (var inertia = new InertiaPreview(null, model.Object))
            using (var collision = new CollisionPreview(null, model.Object, null))
            {
                AddBody(inertia, model, events, "inertia", true);
                AddBody(collision, model, events, "collision", true);
                if (dispose) inertia.Dispose(); else inertia.Hide();
                Assert.False(inertia.IsVisible);
                Assert.True(collision.IsVisible);
                if (dispose) collision.Dispose(); else collision.Hide();
                Assert.False(collision.IsVisible);
            }
            Assert.Equal(new[] { "inertia", "redraw", "collision", "redraw" }, events);
            model.Verify(item => item.GraphicsRedraw2(), Times.Exactly(2));
        }

        private static void ClearBatch(InertiaPreview inertia, CollisionPreview collision,
            Mock<ModelDoc2> model)
        {
            AssemblyExportForm.ClearPreviews(inertia.IsVisible || collision.IsVisible,
                () => inertia.Hide(false), () => collision.Hide(false),
                () => model.Object.GraphicsRedraw2());
        }

        private static Mock<Body2> AddBody(object preview, Mock<ModelDoc2> model,
            List<string> events, string name, bool visible)
        {
            var body = new Mock<Body2>(MockBehavior.Strict);
            body.Setup(item => item.Hide(model.Object)).Callback(() => events.Add(name));
            if (visible)
            {
                var bodies = (List<Body2>)preview.GetType().GetField("temporaryBodies",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
                bodies.Add(body.Object);
            }
            return body;
        }
    }
}
