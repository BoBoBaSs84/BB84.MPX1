using BB84.MPX1.Core.Protocol;

namespace BB84.MPX1.Core.Tests.Protocol;

[TestClass]
public sealed class ControlAddressTests
{
    [TestMethod]
    public void Equality_IsStructural()
    {
        ControlAddress a = new(0, 2, 1);
        ControlAddress b = ControlAddress.Root.Child(0).Child(2).Child(1);

        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, a.Parent());
    }

    [TestMethod]
    public void Default_EqualsRoot()
    {
        ControlAddress empty = default;
        Assert.IsTrue(empty.IsRoot);
        Assert.AreEqual(ControlAddress.Root, empty);
    }

    [TestMethod]
    public void ToString_UsesLevelLetters()
    {
        Assert.AreEqual("A:0 B:2 C:1 D:2", new ControlAddress(0, 2, 1, 2).ToString());
        Assert.AreEqual("(root)", ControlAddress.Root.ToString());
    }
}
