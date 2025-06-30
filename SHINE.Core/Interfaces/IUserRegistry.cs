using System.Collections.Generic;

namespace SHINE.Core
{

        public interface IUserRegistry
        {
            void RegisterUser(string userId);
            void UnregisterUser(string userId);
            IEnumerable<string> GetKnownUsers();
        }


    
}
