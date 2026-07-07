namespace Cxnet.Sampling;

/// <summary>One throughput sample for an interface: bytes/sec down and up at a moment.</summary>
public readonly record struct NetSample(double DownBytesPerSec, double UpBytesPerSec, System.DateTime Timestamp);
