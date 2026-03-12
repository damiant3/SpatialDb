///////////////////////////////////////////////
namespace NeuralNavigator;

/// <summary>
/// Represents a neighbor token shown in the left panel after selecting a token.
/// </summary>
sealed class NeighborInfo(string token, int tokenId, float distance)
{
    public string Token { get; } = token;
    public int TokenId { get; } = tokenId;
    public float Distance { get; } = distance;
}
