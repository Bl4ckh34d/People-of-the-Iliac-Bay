using System;
using System.IO;
using System.Text;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in fixture-only trace of exact local classifier requests and replies.
    // The full prompt is written only to the explicitly supplied work JSONL path,
    // never to Player.log or ordinary runtime diagnostics.
    internal static class SemanticActionModelCaptureProbe
    {
        private const string CaptureArgument="-peopleoftheiliacbay-semantic-action-model-capture";
        private const string CaptureFile="semantic-action-model-capture.jsonl";
        private static readonly object Gate=new object();
        private static string resolvedPath;
        private static bool pathResolved,writeFailed;
        private static bool ownedProbeCapturePath,semanticCaptureReset;
        private static long recordedCount;
        private static string lastStage,lastClassification;

        internal static long LastTrace(out string stage,out string classification)
        {
            lock(Gate) {stage=lastStage;classification=lastClassification;return recordedCount;}
        }

        public static Action<string,string,string> ForStage(string stage)
        {
            string path;
            if(!TryGetPath(out path))return null;
            if(ownedProbeCapturePath && string.Equals(stage,"route-intent",StringComparison.Ordinal))
                ResetSemanticCaptureAfterProbeStarts(path);
            return (request,response,classification)=>Append(path,stage,request,response,classification);
        }

        private static void ResetSemanticCaptureAfterProbeStarts(string path)
        {
            lock(Gate)
            {
                if(semanticCaptureReset || writeFailed)return;
                semanticCaptureReset=true;
                try {if(File.Exists(path))File.Delete(path);}
                catch(Exception error)
                {
                    writeFailed=true;
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SEMANTIC_ACTION_CAPTURE: result=reset-failed; type="+error.GetType().Name+".");
                }
            }
        }

        private static bool TryGetPath(out string path)
        {
            path=null;
            if(!pathResolved)
            {
                pathResolved=true;
                string[] args=Environment.GetCommandLineArgs();
                bool existingChatFixture=Array.IndexOf(args,"-peopleoftheiliacbay-shop-commerce-chat-probe")>=0 ||
                    Array.IndexOf(args,"-peopleoftheiliacbay-action-chat-probe")>=0 ||
                    Array.IndexOf(args,"-peopleoftheiliacbay-verify-murder-report-chat")>=0;
                bool ownedSemanticFixture=Array.IndexOf(args,"-peopleoftheiliacbay-semantic-conversation-probe")>=0 &&
                    string.Equals(ArgumentValue(args,"-peopleoftheiliacbay-fixture-character"),"PeopleOfTheIliacBayTest-9a5f197b",StringComparison.Ordinal) &&
                    string.Equals(ArgumentValue(args,"-peopleoftheiliacbay-fixture-save"),"People - Gothway Garden",StringComparison.Ordinal);
                bool dialogueProbe=Array.IndexOf(args,"-peopleoftheiliacbay-dialogue-probe")>=0;
                bool knowledgeBrowseOnly=Array.IndexOf(args,"-peopleoftheiliacbay-knowledge-browse-only")>=0;
                bool dialogueClockOnly=Array.IndexOf(args,"-peopleoftheiliacbay-dialogue-clock-only")>=0;
                bool ownedDialogueFixture=dialogueProbe && (knowledgeBrowseOnly ^ dialogueClockOnly) &&
                    string.Equals(ArgumentValue(args,"-peopleoftheiliacbay-fixture-character"),"PeopleOfTheIliacBayTest-9a5f197b",StringComparison.Ordinal) &&
                    string.Equals(ArgumentValue(args,"-peopleoftheiliacbay-fixture-save"),"People - Gothway Garden",StringComparison.Ordinal);
                if(existingChatFixture || ownedSemanticFixture || ownedDialogueFixture)
                {
                    int index=Array.IndexOf(args,CaptureArgument);
                    if(index>=0 && index+1<args.Length && !string.IsNullOrWhiteSpace(args[index+1]))
                    {
                        try
                        {
                            string full=Path.GetFullPath(args[index+1]);
                            DirectoryInfo parent=Directory.GetParent(full);
                        if(string.Equals(Path.GetFileName(full),CaptureFile,StringComparison.OrdinalIgnoreCase) &&
                                parent!=null && string.Equals(parent.Name,"work",StringComparison.OrdinalIgnoreCase) &&
                                Directory.Exists(parent.FullName))
                        {
                            resolvedPath=full;
                            ownedProbeCapturePath=ownedSemanticFixture || ownedDialogueFixture;
                        }
                        }
                        catch(ArgumentException) { }
                        catch(NotSupportedException) { }
                        catch(PathTooLongException) { }
                    }
                }
            }
            path=resolvedPath;
            return !string.IsNullOrEmpty(path);
        }

        private static string ArgumentValue(string[] args,string name)
        {
            int index=Array.IndexOf(args,name);
            return index>=0 && index+1<args.Length?args[index+1]:null;
        }

        private static void Append(string path,string stage,string request,string response,string classification)
        {
            lock(Gate)
            {
                if(writeFailed)return;
                try
                {
                    string line="{\"stage\":\""+Escape(stage)+"\",\"requestJson\":\""+Escape(request)+
                        "\",\"responseJson\":\""+Escape(response)+"\",\"classification\":\""+Escape(classification)+"\"}"+
                        Environment.NewLine;
                    File.AppendAllText(path,line,new UTF8Encoding(false));
                    lastStage=stage;lastClassification=classification;recordedCount++;
                }
                catch(Exception error)
                {
                    writeFailed=true;
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SEMANTIC_ACTION_CAPTURE: result=write-failed; type="+error.GetType().Name+".");
                }
            }
        }

        private static string Escape(string value)
        {return LocalDialogueCompletion.EscapeJson(value??string.Empty);}
    }
}
