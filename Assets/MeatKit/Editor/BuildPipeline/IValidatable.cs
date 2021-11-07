using System.Collections.Generic;

namespace MeatKit
{
    public interface IValidatable
    {
        Dictionary<string, BuildMessage> Validate();
    }
}
