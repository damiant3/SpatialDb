using System.Numerics;
///////////////////////////////////////////////
namespace NeuralNavigator;

/// <summary>
/// Represents a single generated token with its 3D position in embedding space
/// and the hidden-state movement distance from the previous step.
/// </summary>
sealed class GenerationTokenInfo
{
    public string Text { get; }
    public int TokenId { get; }
    public int StepIndex { get; }
    public Vector3 Position { get; }
    public float MovementDistance { get; }
    public bool IsPromptToken { get; }

    public string DisplayText => IsPromptToken ? $"[{Text}]" : Text;
    public string Tooltip => $"\"{Text}\"  ID:{TokenId}  Step:{StepIndex}  Move:{MovementDistance:F1}";

    public GenerationTokenInfo(string text, int tokenId, int stepIndex,
        Vector3 position, float movementDistance, bool isPromptToken)
    {
        Text = text;
        TokenId = tokenId;
        StepIndex = stepIndex;
        Position = position;
        MovementDistance = movementDistance;
        IsPromptToken = isPromptToken;
    }
}
