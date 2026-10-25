namespace DaggerfallWorkshop.Game.MagicAndEffects
{
    public interface IEntityEffect { }
    public class BaseEntityEffect : IEntityEffect { public string Key { get; set; } }
}

namespace DaggerfallWorkshop.Game.Entity
{
    public class DaggerfallEntityBehaviour
    {
        public static implicit operator bool(DaggerfallEntityBehaviour value) { return value != null; }
    }
}

namespace DaggerfallWorkshop.Game
{
    public sealed class GameManager
    {
        public static GameManager Instance { get; set; } = new GameManager();
        public DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour PlayerEntityBehaviour { get; set; }
    }
}

namespace DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects
{
    public abstract class DrainEffect : DaggerfallWorkshop.Game.MagicAndEffects.BaseEntityEffect { }
}
