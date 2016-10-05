﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InformedProteomics.Backend.Data.Enum;

namespace InformedProteomics.Backend.Data.Sequence
{
    public class ModFileParser
    {
        public ModFileParser(string modFilePath)
        {
            _modFilePath = modFilePath;
            _searchModifications = Parse(modFilePath, out _maxNumDynModsPerSequence);
        }

        public string ModFilePath
        {
            get { return _modFilePath;  }
        }

        public IEnumerable<SearchModification> SearchModifications
        {
            get { return _searchModifications; }
        }

        public int MaxNumDynModsPerSequence
        {
            get { return _maxNumDynModsPerSequence;  }
        }

        private readonly string _modFilePath;
        private readonly IEnumerable<SearchModification> _searchModifications;
        private readonly int _maxNumDynModsPerSequence;

        public static List<SearchModification> ParseModification(string line)
        {
            var token = line.Split(',');
            if (token.Length != 5) return null;

            // Composition

            var compStr = token[0].Trim();
            var composition = Composition.Composition.ParseFromPlainString(compStr) ??
                              Composition.Composition.Parse(compStr);
            if (composition == null)
            {
                throw new Exception("Illegal Composition");
            }

            // Residues
            var residueStr = token[1].Trim();
            var isResidueStrLegitimate = residueStr.Equals("*")
                || residueStr.Any() && residueStr.All(AminoAcid.IsStandardAminoAcidResidue);
            if (!isResidueStrLegitimate)
            {
                throw new Exception("Illegal residues at line");
            }

            // isFixedModification
            bool isFixedModification;
            if (token[2].Trim().Equals("fix", StringComparison.InvariantCultureIgnoreCase)) isFixedModification = true;
            else if (token[2].Trim().Equals("opt", StringComparison.InvariantCultureIgnoreCase)) isFixedModification = false;
            else
            {
                throw new Exception("Illegal modification type (fix or opt)");
            }

            // Location
            SequenceLocation location;
            var locStr = token[3].Trim().Split()[0];
            if (locStr.Equals("any", StringComparison.InvariantCultureIgnoreCase))
                location = SequenceLocation.Everywhere;
            else if (locStr.Equals("N-Term", StringComparison.InvariantCultureIgnoreCase) ||
                     locStr.Equals("NTerm", StringComparison.InvariantCultureIgnoreCase))
                location = SequenceLocation.PeptideNTerm;
            else if (locStr.Equals("C-Term", StringComparison.InvariantCultureIgnoreCase) ||
                     locStr.Equals("CTerm", StringComparison.InvariantCultureIgnoreCase))
                location = SequenceLocation.PeptideCTerm;
            else if (locStr.Equals("Prot-N-Term", StringComparison.InvariantCultureIgnoreCase) ||
                     locStr.Equals("ProtNTerm", StringComparison.InvariantCultureIgnoreCase))
                location = SequenceLocation.ProteinNTerm;
            else if (locStr.Equals("Prot-C-Term", StringComparison.InvariantCultureIgnoreCase) ||
                     locStr.Equals("ProtCTerm", StringComparison.InvariantCultureIgnoreCase))
                location = SequenceLocation.ProteinCTerm;
            else
            {
                throw new Exception("{0}: Illegal modification location (fix or opt)");
            }

            // Check if it's valid
            if (residueStr.Equals("*") && location == SequenceLocation.Everywhere)
            {
                throw new Exception("Invalid modification: * should not be applied to \"any\"");
            }

            var name = token[4].Split()[0].Trim();

            var mod = Modification.Get(name) ?? Modification.RegisterAndGetModification(name, composition);
            return residueStr.Select(residue => new SearchModification(mod, residue, location, isFixedModification)).ToList();
        }

        public static List<SearchModification> Parse(IEnumerable<string> lines, out int maxNumDynModsPerPeptide)
        {
            var searchModList = new List<SearchModification>();
            int lineNum = 0;
            maxNumDynModsPerPeptide = 0;
            foreach (var line in lines)
            {
                lineNum++;
                var tokenArr = line.Split('#');
                if (tokenArr.Length == 0) continue;
                var s = tokenArr[0].Trim();
                if (s.Length == 0) continue;

                if (s.StartsWith("NumMods="))
                {
                    try
                    {
                        maxNumDynModsPerPeptide = Convert.ToInt32(s.Split('=')[1].Trim());
                    }
                    catch (FormatException)
                    {
                        throw new Exception(string.Format("Illegal NumMods parameter at line {0}", lineNum));
                    }
                }
                else
                {
                    IEnumerable<SearchModification> mods = null;
                    try
                    {
                        mods = ParseModification(line);
                    }
                    catch (Exception e)
                    {
                        throw new Exception(string.Format("{0} at line {1}.", e.Message, lineNum), e);
                    }

                    searchModList.AddRange(mods);
                }
            }

            return searchModList;
        }

        private static IEnumerable<SearchModification> Parse(string modFilePath, out int maxNumDynModsPerPeptide)
        {
            var searchModList = new List<SearchModification>();
            var lines = File.ReadLines(modFilePath);
            try
            {
                searchModList = Parse(lines, out maxNumDynModsPerPeptide);
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("{0}: {1}", modFilePath, e.Message), e);
            }

            return searchModList;
        }
    }
}
