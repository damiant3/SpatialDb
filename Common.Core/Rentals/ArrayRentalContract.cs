using System.Buffers;
///////////////////////////////
namespace Common.Core.Rentals;

public class ArrayRentalContract<T>(T[] array)
    : IDisposable
{
    private bool m_disposed = false;
    private readonly T[] m_array = array;
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        if (m_array != null)
            ArrayPool<T>.Shared.Return(m_array, clearArray: false);
        GC.SuppressFinalize(this);
    }
}

public static class ArrayRental
{
    public static ArrayRentalContract<T> Rent<T>(int length, out T[] array)
    {
        array = ArrayPool<T>.Shared.Rent(length);
        for (int i = 0; i < length; i++) array[i] = default!;
        return new ArrayRentalContract<T>(array);
    }
}
