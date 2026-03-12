///////////////////////////////////////////////
namespace NeuralNavigator;

sealed class NeighborInfo(string token, int tokenId, float distance)
{
    public string Token { get; } = token;
    public int TokenId { get; } = tokenId;
    public float Distance { get; } = distance;
}
