using System.Buffers;
///////////////////////////////
namespace SpatialDbLib.Lattice;

public class ArrayRentalContract(byte[] array) 
    : IDisposable
{
    private readonly byte[] m_array = array;

    public void Dispose()
    {
        if (m_array != null)
        {
            ArrayPool<byte>.Shared.Return(m_array, clearArray: false);
        }
        GC.SuppressFinalize(this);
    }
}
