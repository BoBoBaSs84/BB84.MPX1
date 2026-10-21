namespace BB84.MPX1.Midi.Tests;

[TestClass]
public sealed class SysExAssemblerTests
{
    [TestMethod]
    [Description("TRN-004: frame split across buffers")]
    public void Feed_SplitFrame_IsReassembled()
    {
        SysExAssembler assembler = new();

        Assert.IsEmpty(assembler.Feed([0xF0, 0x06, 0x09]));
        IReadOnlyList<byte[]> frames = assembler.Feed([0x00, 0x12, 0x01, 0x00, 0xF7]);

        Assert.HasCount(1, frames);
        CollectionAssert.AreEqual(new byte[] { 0xF0, 0x06, 0x09, 0x00, 0x12, 0x01, 0x00, 0xF7 }, frames[0]);
    }

    [TestMethod]
    [Description("TRN-004: several frames in one buffer, garbage and real-time bytes ignored")]
    public void Feed_MultipleFramesWithNoise()
    {
        SysExAssembler assembler = new();

        IReadOnlyList<byte[]> frames = assembler.Feed([0x01, 0xF0, 0x01, 0xF8, 0x02, 0xF7, 0x55, 0xF0, 0x03, 0xF7]);

        Assert.HasCount(2, frames);
        CollectionAssert.AreEqual(new byte[] { 0xF0, 0x01, 0x02, 0xF7 }, frames[0]);
        CollectionAssert.AreEqual(new byte[] { 0xF0, 0x03, 0xF7 }, frames[1]);
    }

    [TestMethod]
    [Description("A new F0 restarts the frame; another status byte aborts it")]
    public void Feed_InterruptedFrames()
    {
        SysExAssembler assembler = new();

        IReadOnlyList<byte[]> frames = assembler.Feed([0xF0, 0x01, 0xF0, 0x02, 0xF7, 0xF0, 0x03, 0x90, 0x04, 0xF7]);

        Assert.HasCount(1, frames);
        CollectionAssert.AreEqual(new byte[] { 0xF0, 0x02, 0xF7 }, frames[0]);
    }

    [TestMethod]
    [Description("SEC-001: oversized frames are dropped")]
    public void Feed_OversizedFrame_IsDropped()
    {
        SysExAssembler assembler = new();
        byte[] big = new byte[SysExAssembler.MaxFrameLength + 10];
        big[0] = 0xF0;
        big[^1] = 0xF7;

        Assert.IsEmpty(assembler.Feed(big));
        Assert.AreEqual(1, assembler.DroppedFrames);
    }
}
