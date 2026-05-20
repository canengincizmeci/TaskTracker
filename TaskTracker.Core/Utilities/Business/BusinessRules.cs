using System;
using System.Collections.Generic;
using System.Text;
using TaskTracker.Core.Utilities.Results;

namespace TaskTracker.Core.Utilities.Business
{
    public class BusinessRules
    {
        public static IResult Run(params IResult[] logics)
        {
            foreach (var logic in logics)
            {

                if (!logic.Success)
                {
                    return logic;
                }

            }


            return null;
        }
        public static async Task<IResult?> RunAsync(params Task<IResult>[] logics)
        {
            foreach (var logic in logics)
            {
                var result = await logic;
                if (!result.Success)
                    return result;
            }
            return null;
        }
    }
}
