namespace Orbit.Services;

/// <summary>A composed view owns cleanup of its children, but can borrow a shared view model.</summary>
public interface IToolViewLifetime : System.IDisposable { }
