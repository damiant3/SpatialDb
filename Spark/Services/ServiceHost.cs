using System.Diagnostics;
using System.Reflection;
///////////////////////////////////////////////
namespace Spark.Services;

/// <summary>
/// Lightweight service locator. Services are registered at startup and resolved by type.
/// Thread-safe for reads after initialization. No external DI framework required.
/// </summary>
sealed class ServiceHost : IDisposable
{
    static ServiceHost? s_instance;
    public static ServiceHost Instance => s_instance ?? throw new InvalidOperationException("ServiceHost not initialized.");
    public static bool IsInitialized => s_instance is not null;

    readonly Dictionary<Type, object> m_services = [];
    readonly Dictionary<Type, Func<object>> m_factories = [];
    readonly object m_sync = new();

    ServiceHost() { }

    public static ServiceHost Initialize()
    {
        s_instance ??= new ServiceHost();
        return s_instance;
    }

    /// <summary>Register a singleton instance.</summary>
    public ServiceHost Register<T>(T instance) where T : class
    {
        lock (m_sync) m_services[typeof(T)] = instance;
        return this;
    }

    /// <summary>Register a lazy factory (called once on first resolve, then cached).</summary>
    public ServiceHost RegisterFactory<T>(Func<T> factory) where T : class
    {
        lock (m_sync) m_factories[typeof(T)] = () => factory();
        return this;
    }

    /// <summary>Register a concrete type for auto-wired construction.</summary>
    public ServiceHost RegisterType<TService, TImpl>() where TService : class where TImpl : class, TService
    {
        lock (m_sync) m_factories[typeof(TService)] = () => CreateInstance(typeof(TImpl));
        return this;
    }

    public ServiceHost RegisterType<T>() where T : class
    {
        lock (m_sync) m_factories[typeof(T)] = () => CreateInstance(typeof(T));
        return this;
    }

    /// <summary>Resolve a service. Returns null if not registered.</summary>
    public T? Get<T>() where T : class
    {
        lock (m_sync)
        {
            Type key = typeof(T);
            if (m_services.TryGetValue(key, out object? svc))
                return (T)svc;

            if (m_factories.TryGetValue(key, out Func<object>? factory))
            {
                T instance = (T)factory();
                m_services[key] = instance;
                m_factories.Remove(key);
                return instance;
            }

            // Check for assignable factories
            Type? assignable = m_factories.Keys.FirstOrDefault(key.IsAssignableFrom);
            if (assignable is not null)
            {
                object obj = m_factories[assignable]();
                if (obj is T typed)
                {
                    m_services[key] = typed;
                    m_factories.Remove(assignable);
                    return typed;
                }
            }
        }
        return null;
    }

    public T Require<T>() where T : class
        => Get<T>() ?? throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");

    object CreateInstance(Type implType)
    {
        IOrderedEnumerable<ConstructorInfo> ctors = implType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length);

        foreach (ConstructorInfo ctor in ctors)
        {
            ParameterInfo[] parms = ctor.GetParameters();
            object[] args = new object[parms.Length];
            bool ok = true;
            for (int i = 0; i < parms.Length; i++)
            {
                if (parms[i].HasDefaultValue)
                    args[i] = parms[i].DefaultValue!;
                else
                {
                    try { args[i] = ResolveOrThrow(parms[i].ParameterType); }
                    catch { ok = false; break; }
                }
            }
            if (ok) return ctor.Invoke(args);
        }
        throw new InvalidOperationException($"No constructor could be satisfied for {implType.FullName}.");
    }

    object ResolveOrThrow(Type t)
    {
        lock (m_sync)
        {
            if (m_services.TryGetValue(t, out object? svc)) return svc;
            if (m_factories.TryGetValue(t, out Func<object>? factory))
            {
                object instance = factory();
                m_services[t] = instance;
                m_factories.Remove(t);
                return instance;
            }
            Type? assignable = m_factories.Keys.FirstOrDefault(t.IsAssignableFrom);
            if (assignable is not null)
            {
                object obj = m_factories[assignable]();
                m_services[t] = obj;
                m_factories.Remove(assignable);
                return obj;
            }
        }
        throw new InvalidOperationException($"Service {t.Name} is not registered.");
    }

    public void Dispose()
    {
        lock (m_sync)
        {
            foreach (object svc in m_services.Values)
            {
                try { (svc as IDisposable)?.Dispose(); }
                catch (Exception ex) { Trace.TraceError($"ServiceHost.Dispose: {ex}"); }
            }
            m_services.Clear();
            m_factories.Clear();
        }
        s_instance = null;
    }
}
