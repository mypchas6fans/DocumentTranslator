﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TranslationAssistant.Business
{

    class Utterances : List<Utterances>
    {
        public List<Utterance> UtteranceList { get; set; }
        public string langcode { get; set; }

        private List<Utterance> UtteranceResultList = new List<Utterance>();

        public Utterances(List<Utterance> utterances)
        {
            UtteranceList = utterances;
        }

        public Utterances()
        {

        }

        public Utterances(string languagecode)
        {
            langcode = languagecode;
        }


        /// <summary>
        /// Distribute the New Text to the utterances that mirrors 
        /// the lengths of the original utterances.
        /// </summary>
        /// <param name="newtext">The new text.</param>
        /// <returns>The list of utterances containing the new text.</returns>
        public async Task<List<Utterance>> Distribute(List<string> newtext)
        {

            //Sum the lengths of each original utterance within the group of utterances. Groups are separated by a blank utterance.
            //Two passes: First assign groups, then sum per group and calculate portions per utterance
            int groupsumlength = 0;
            int groupnum = 0;
            List<int> groupsums = new List<int>();

            //assign groups and calculate group sum
            for (int i =0; i< UtteranceList.Count; i++)
            {
                UtteranceList[i].group = groupnum;
                if (UtteranceList[i].lines == 0)
                {
                    groupsums.Add(groupsumlength);
                    groupnum++;
                    groupsumlength = 0;
                }
                else
                {
                    groupsumlength += UtteranceList[i].content.Length;
                }
            }

            //Calculate the ratio of each utterance per group. 
            for (int i = 0; i < UtteranceList.Count; i++)
            {
                if ((UtteranceList[i].content.Length < 1) || (groupsums[UtteranceList[i].group] == 0))
                {
                    UtteranceList[i].portion = 0;
                    UtteranceList[i].lines = 0;
                }
                else
                {
                    UtteranceList[i].portion = (double)UtteranceList[i].content.Length / groupsums[UtteranceList[i].group];
                }
            }

            string remainingstring = string.Empty;

            //generate sentence breaks
            List<Task<List<int>>> tasklist = new List<Task<List<int>>>();
            foreach (string line in newtext)
            {
                Task<List<int>> t = TranslationServices.Core.TranslationServiceFacade.BreakSentencesAsync(line, langcode);
                tasklist.Add(t);
            }
            List<int>[] sentencebreaks = await Task.WhenAll(tasklist);

            //split the new text per ratio
            for (int groupindex=0; groupindex<newtext.Count; groupindex++)
            {
                //get all utterances for this group
                IEnumerable<Utterance> grouplist = UtteranceList.Where(utterance => utterance.group == groupindex);

                List<double> portions = new List<double>();
                foreach (var item in grouplist)
                {
                    portions.Add(item.portion);
                }
                List<string> splitlist = SplitString2(newtext[groupindex], portions);
                List<Utterance> newgrouplist = grouplist.ToList();
                for (int utteranceindex = 0; utteranceindex < grouplist.Count(); utteranceindex++)
                {
                    Utterance utt = newgrouplist[utteranceindex];
                    utt.content = splitlist[utteranceindex];
                    UtteranceResultList.Add(utt);
                }
            }


            return UtteranceResultList;
        }

        private List<string> SplitString2(string text, List<double> portions)
        {
            List<double> normalizedportions = NormalizeDistribution(portions);
            List<string> resultlist = new List<string>(portions.Count);
            if (portions.Count <= 1 )
            {
                resultlist.Add(text);
                return resultlist;
            }
            int firstbreak = FindClosestWordBreak(text, (int) (normalizedportions[0] * text.Length));
            resultlist.Add(text.Substring(0, firstbreak));
            Debug.WriteLine("Resultlist added: " + text.Substring(0, firstbreak));
            string remainder = text.Substring(firstbreak);
            List<double> newportions = new List<double>(portions.Count);
            newportions = portions;
            newportions.RemoveAt(0);
            resultlist.AddRange(SplitString2(remainder, newportions));
            return resultlist;
        }



        /// <summary>
        /// Splits a text into uneven sized portions, trying to hit sentence breaks within tolerance
        /// </summary>
        /// <param name="text">The text to split</param>
        /// <param name="sentencebreaks">An array of sentence breaks</param>
        /// <param name="distribution">An array of facors for the relative length of each portion</param>
        /// <param name="tolerance">The tolerance for sentence breaks relative to string length. Higher than tolerance goes to word break.</param>
        /// <returns>Array of strings split by the distribution. Number of rows matches the length of the distribution array.</returns>
        private List<string> SplitString(string text, List<int> sentencebreaks, List<double> distribution, float tolerance)
        {
            List<string> candidates = new List<string>(distribution.Count);
            if (distribution.Count <= 1)
            {
                candidates.Add(text);
                return candidates;
            }
            List<double> normalizeddistribution = NormalizeDistribution(distribution);

            //
            string remainingtext = text;
            for (int index = 0; index < distribution.Count; index++)
            {
                int targetlength = (int)normalizeddistribution[index] * text.Length;
                if (targetlength >= text.Length)
                {
                    candidates.Add(text);
                    continue;
                }
                int brk = FindClosest(sentencebreaks, targetlength);
                if (Math.Abs(brk - targetlength) > (targetlength * 1.25))
                {
                    brk = FindClosestWordBreak(remainingtext, targetlength);
                }
                candidates.Add(remainingtext.Substring(0, brk));
                remainingtext = remainingtext.Substring(brk);
            }
            return candidates;
        }

        private static List<double> NormalizeDistribution(List<double> distribution)
        {
            //Normalize the distribution
            double sumdistribution = 0;
            foreach (var dist in distribution) sumdistribution += dist;
            List<double> normalizeddistribution = new List<double>();
            foreach (var dist in distribution)
            {
                normalizeddistribution.Add(dist / sumdistribution);
            }
            return normalizeddistribution;
        }

        private int FindClosest(List<int> breaks, int targetlength)
        {
            if (breaks.Count<1) return 0;
            int candidate = breaks[0];
            int newcandidate = candidate;
            for (int i = 1; i < breaks.Count; i++)
            {
                newcandidate += breaks[i];
                if (Math.Abs(newcandidate - targetlength) < Math.Abs(candidate - targetlength)) candidate = newcandidate;
            }
            return candidate;

        }

        /// <summary>
        /// Returns offset of the closest sentence break
        /// </summary>
        /// <param name="text">text to analyze</param>
        /// <param name="targetlength">Target length</param>
        /// <returns></returns>
        private async Task<int> FindClosestSentenceBreak(string text, int targetlength)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            List<int> sentencebreaks = await TranslationServices.Core.TranslationServiceFacade.BreakSentencesAsync(text, langcode);
            int candidate = sentencebreaks[0];
            int newcandidate = candidate;
            for (int i=1; i<sentencebreaks.Count; i++)
            {
                newcandidate += sentencebreaks[i];
                if (Math.Abs(newcandidate - targetlength) < Math.Abs(candidate - targetlength)) candidate = newcandidate;
            }
            return candidate;
        }


        private string SplitLines(string thistext, int lines)
        {
            if (String.IsNullOrEmpty(thistext) || (lines < 1)) return string.Empty;
            if (lines == 1) return thistext;
            StringBuilder result = new StringBuilder();
            int avgLength = (int) (thistext.Length / lines);
            string remainingtext = thistext;
            for (int i=0; i<lines; i++)
            {
                int endindex = FindClosestWordBreak(remainingtext, avgLength);
                string interim = remainingtext.Substring(0, endindex);
                interim = interim .Replace("\r\n", " ");
                interim = interim.Replace("\n", " ");
                interim = interim.Replace("\n", " ");
                result.AppendLine(interim.Trim());
                remainingtext = remainingtext.Substring(endindex);
                Debug.WriteLine("SplitLines.remainingtext: {0}", remainingtext);
            }

            return result.ToString();
        }

        private int FindClosestWordBreak(string input, int targetlength)
        {
            Random random = new Random();
            if ((input.Length <= (targetlength + 2)) || (input.Length <=2) || (input.Length <= (targetlength - 2))) return input.Length;
            if (IsBreakCharacter(input[targetlength])) return targetlength;
            for (int i=1; i<targetlength; i++)
            {
                if ((targetlength + i) >= input.Length) return targetlength + i;
                if (IsBreakCharacter(input[targetlength + i])) return targetlength + i;
                if ((i < input.Length) && IsBreakCharacter(input[targetlength - i])) return targetlength - i;
            }
            return targetlength;
        }

        private bool IsBreakCharacter(char ch)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
            switch (category)
            {
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.OtherSymbol:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.SpaceSeparator:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsPunctuation(char ch)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
            switch (category)
            {
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.OtherSymbol:
                case UnicodeCategory.ParagraphSeparator:
                    return true;
                default:
                    return false;
            }
        }




        /// <summary>
        /// Calculates the number of newlines in a string.
        /// </summary>
        /// <param name="content">Text Content to count</param>
        /// <returns>Number of lines</returns>
        private int CountNewlines(string content)
        {
            if (content.Length < 1) return 0;
            int count = 1;
            foreach (char c in content)
            {
                if (c == '\n') count++;
            }
            return count;
        }
    }
}
