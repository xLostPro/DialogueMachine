#include "DialogueMachineRules.h"
#include "Misc/Char.h"

const FString FDialogueRules::Exit = TEXT("[EXIT]");
const FString FDialogueRules::NoDialogue = TEXT("(No dialogue)");

bool FDialogueRules::Compare(int32 Actual, const FString& Op, int32 Target)
{
    if (Op == TEXT("==")) return Actual == Target;
    if (Op == TEXT("!=")) return Actual != Target;
    if (Op == TEXT(">")) return Actual > Target;
    if (Op == TEXT("<")) return Actual < Target;
    if (Op == TEXT(">=")) return Actual >= Target;
    if (Op == TEXT("<=")) return Actual <= Target;
    return false;
}

FString FDialogueRules::Key(const FString& Type, const FString& Id)
{
    return (Type == TEXT("Inventory") ? TEXT("inv:") : TEXT("aff:")) + Id;
}

// Mirrors int.TryParse with NumberStyles.Integer: surrounding whitespace and one sign are allowed, overflow is not.
static bool ParseIntExact(const FString& Raw, int32& OutValue)
{
    const FString Str = Raw.TrimStartAndEnd();
    if (Str.IsEmpty()) return false;
    int32 Start = 0;
    if (Str[0] == TEXT('-') || Str[0] == TEXT('+'))
    {
        Start = 1;
        if (Str.Len() == 1) return false;
    }
    for (int32 i = Start; i < Str.Len(); ++i)
    {
        if (!FChar::IsDigit(Str[i])) return false;
    }
    if (Str.Len() - Start > 10) return false;
    const int64 Parsed = FCString::Atoi64(*Str);
    if (Parsed < TNumericLimits<int32>::Min() || Parsed > TNumericLimits<int32>::Max()) return false;
    OutValue = (int32)Parsed;
    return true;
}

bool FDialogueRules::RequirementMet(const FString& Type, const FString& TargetId, const FString& Op, const FString& Value, const TMap<FString, int32>& Vars, FString& OutReason)
{
    int32 Target = 0;
    if ((Type != TEXT("Affinity") && Type != TEXT("Inventory")) || TargetId.TrimStartAndEnd().IsEmpty() || !ParseIntExact(Value, Target))
    {
        OutReason = TEXT("This condition is incomplete or invalid.");
        return false;
    }

    FString ReqKey = Key(Type, TargetId);
    const int32* Found = Vars.Find(ReqKey);
    int32 Actual = Found ? *Found : 0;
    if (!Compare(Actual, Op, Target))
    {
        OutReason = FString::Printf(TEXT("Requires %s %s %d · current %d"), *ReqKey, *Op, Target, Actual);
        return false;
    }

    OutReason = TEXT("");
    return true;
}

bool FDialogueRules::RequirementsMet(const FDialogueChoice& Choice, const TMap<FString, int32>& Vars, FString& OutReason)
{
    const bool bAny = Choice.bAnyRequirement && Choice.Requirements.Num() > 0;
    FString FirstReason;
    bool bHasReason = false;
    for (const FDialogueRequirement& Req : Choice.Requirements)
    {
        FString Why;
        if (RequirementMet(Req.Type, Req.TargetId, Req.Operator, Req.Value, Vars, Why))
        {
            if (bAny)
            {
                OutReason = TEXT("");
                return true;
            }
        }
        else if (!bAny)
        {
            OutReason = Why;
            return false;
        }
        else if (!bHasReason)
        {
            FirstReason = Why;
            bHasReason = true;
        }
    }
    OutReason = FirstReason;
    return !bAny;
}

static bool IsKeyChar(TCHAR C)
{
    return FChar::IsAlnum(C) || C == TEXT('_') || C == TEXT(':') || C == TEXT('.') || C == TEXT('-');
}

static bool TryParseIfHeader(const FString& Text, int32 Start, int32& OutHeaderEnd, FString& OutKey, FString& OutOp, int32& OutTarget, bool& bOutTargetValid)
{
    int32 Len = Text.Len();
    if (Start + 4 > Len) return false;
    if (Text[Start] != TEXT('[')) return false;

    // Check [if with whitespace
    if (FChar::ToLower(Text[Start + 1]) != TEXT('i') || FChar::ToLower(Text[Start + 2]) != TEXT('f')) return false;
    if (!FChar::IsWhitespace(Text[Start + 3])) return false;

    int32 Pos = Start + 4;
    while (Pos < Len && FChar::IsWhitespace(Text[Pos])) ++Pos;

    int32 KeyStart = Pos;
    while (Pos < Len && IsKeyChar(Text[Pos])) ++Pos;
    if (Pos == KeyStart) return false;
    OutKey = Text.Mid(KeyStart, Pos - KeyStart);

    while (Pos < Len && FChar::IsWhitespace(Text[Pos])) ++Pos;

    int32 OpStart = Pos;
    if (Pos + 1 < Len)
    {
        FString TwoChar = Text.Mid(Pos, 2);
        if (TwoChar == TEXT("==") || TwoChar == TEXT("!=") || TwoChar == TEXT(">=") || TwoChar == TEXT("<="))
        {
            OutOp = TwoChar;
            Pos += 2;
        }
    }
    if (OutOp.IsEmpty() && Pos < Len)
    {
        TCHAR OneChar = Text[Pos];
        if (OneChar == TEXT('>') || OneChar == TEXT('<'))
        {
            OutOp = FString(1, &OneChar);
            Pos += 1;
        }
    }
    if (OutOp.IsEmpty()) return false;

    while (Pos < Len && FChar::IsWhitespace(Text[Pos])) ++Pos;

    // The C# pattern is (-?\d+)\] : an optional minus, digits, and the bracket right after them.
    int32 NumStart = Pos;
    if (Pos < Len && Text[Pos] == TEXT('-')) ++Pos;
    int32 DigitStart = Pos;
    while (Pos < Len && FChar::IsDigit(Text[Pos])) ++Pos;
    if (Pos == DigitStart) return false;

    if (Pos >= Len || Text[Pos] != TEXT(']')) return false;

    // A number that doesn't fit in an int32 still forms a block, but the condition counts as false.
    bOutTargetValid = ParseIntExact(Text.Mid(NumStart, Pos - NumStart), OutTarget);

    OutHeaderEnd = Pos;
    return true;
}

static int32 FindNextIfWithSpace(const FString& Text, int32 Start)
{
    int32 Pos = Start;
    while (Pos + 3 < Text.Len())
    {
        if (Text[Pos] == TEXT('[') &&
            FChar::ToLower(Text[Pos + 1]) == TEXT('i') &&
            FChar::ToLower(Text[Pos + 2]) == TEXT('f') &&
            FChar::IsWhitespace(Text[Pos + 3]))
        {
            return Pos;
        }
        ++Pos;
    }
    return INDEX_NONE;
}

static int32 FindNextCloseIf(const FString& Text, int32 Start)
{
    int32 Pos = Start;
    while (Pos + 4 < Text.Len())
    {
        if (Text[Pos] == TEXT('[') &&
            Text[Pos + 1] == TEXT('/') &&
            FChar::ToLower(Text[Pos + 2]) == TEXT('i') &&
            FChar::ToLower(Text[Pos + 3]) == TEXT('f') &&
            Text[Pos + 4] == TEXT(']'))
        {
            return Pos;
        }
        ++Pos;
    }
    return INDEX_NONE;
}

static int32 FindElseTag(const FString& Text)
{
    int32 Pos = 0;
    while (Pos + 5 < Text.Len())
    {
        if (Text[Pos] == TEXT('[') &&
            FChar::ToLower(Text[Pos + 1]) == TEXT('e') &&
            FChar::ToLower(Text[Pos + 2]) == TEXT('l') &&
            FChar::ToLower(Text[Pos + 3]) == TEXT('s') &&
            FChar::ToLower(Text[Pos + 4]) == TEXT('e') &&
            Text[Pos + 5] == TEXT(']'))
        {
            return Pos;
        }
        ++Pos;
    }
    return INDEX_NONE;
}

FString FDialogueRules::ResolveText(const FString& Text, const TMap<FString, int32>& Vars, const TMap<FString, FString>& TextVars)
{
    FString CurrentText = Text;

    // Up to 64 passes of innermost conditional resolution
    for (int32 Pass = 0; Pass < 64; ++Pass)
    {
        bool bChanged = false;
        FString NewText;
        int32 ScanPos = 0;

        while (ScanPos < CurrentText.Len())
        {
            int32 IfStart = FindNextIfWithSpace(CurrentText, ScanPos);
            if (IfStart == INDEX_NONE)
            {
                NewText += CurrentText.Mid(ScanPos);
                break;
            }

            NewText += CurrentText.Mid(ScanPos, IfStart - ScanPos);

            int32 HeaderEnd = INDEX_NONE;
            FString Key;
            FString Op;
            int32 Target = 0;
            bool bTargetValid = false;

            if (TryParseIfHeader(CurrentText, IfStart, HeaderEnd, Key, Op, Target, bTargetValid))
            {
                int32 ClosePos = FindNextCloseIf(CurrentText, HeaderEnd + 1);
                int32 NextIf = FindNextIfWithSpace(CurrentText, HeaderEnd + 1);

                if (ClosePos != INDEX_NONE && (NextIf == INDEX_NONE || NextIf > ClosePos))
                {
                    // Innermost block found!
                    FString Body = CurrentText.Mid(HeaderEnd + 1, ClosePos - (HeaderEnd + 1));
                    int32 ElsePos = FindElseTag(Body);
                    FString TrueBranch;
                    FString FalseBranch;
                    if (ElsePos != INDEX_NONE)
                    {
                        // Like Regex.Split, only the text up to the next [else] belongs to the false branch.
                        TrueBranch = Body.Left(ElsePos);
                        FalseBranch = Body.Mid(ElsePos + 6);
                        const int32 SecondElse = FindElseTag(FalseBranch);
                        if (SecondElse != INDEX_NONE)
                        {
                            FalseBranch = FalseBranch.Left(SecondElse);
                        }
                    }
                    else
                    {
                        TrueBranch = Body;
                    }

                    int32 Value = 0;
                    if (const int32* Found = Vars.Find(Key))
                    {
                        Value = *Found;
                    }
                    else if (const int32* FoundInv = Vars.Find(TEXT("inv:") + Key))
                    {
                        Value = *FoundInv;
                    }
                    else if (const int32* FoundAff = Vars.Find(TEXT("aff:") + Key))
                    {
                        Value = *FoundAff;
                    }

                    if (bTargetValid && Compare(Value, Op, Target))
                    {
                        NewText += TrueBranch;
                    }
                    else
                    {
                        NewText += FalseBranch;
                    }

                    ScanPos = ClosePos + 5;
                    bChanged = true;
                    continue;
                }
            }

            // Not an innermost block or not a valid header: output up to next character and advance
            NewText += CurrentText[IfStart];
            ScanPos = IfStart + 1;
        }

        CurrentText = NewText;
        if (!bChanged)
        {
            break;
        }
    }

    // Placeholder substitutions: {key}
    FString Substituted;
    int32 Pos = 0;
    while (Pos < CurrentText.Len())
    {
        if (CurrentText[Pos] == TEXT('{'))
        {
            int32 CloseBrace = CurrentText.Find(TEXT("}"), ESearchCase::CaseSensitive, ESearchDir::FromStart, Pos + 1);
            if (CloseBrace != INDEX_NONE)
            {
                FString Key = CurrentText.Mid(Pos + 1, CloseBrace - (Pos + 1));
                bool bValidKey = !Key.IsEmpty();
                for (TCHAR C : Key)
                {
                    if (!IsKeyChar(C))
                    {
                        bValidKey = false;
                        break;
                    }
                }

                if (bValidKey)
                {
                    if (const FString* TextVal = TextVars.Find(Key))
                    {
                        Substituted += *TextVal;
                        Pos = CloseBrace + 1;
                        continue;
                    }
                    else if (const int32* NumVal = Vars.Find(Key))
                    {
                        Substituted += FString::FromInt(*NumVal);
                        Pos = CloseBrace + 1;
                        continue;
                    }
                }
            }
        }

        Substituted += CurrentText[Pos];
        ++Pos;
    }

    return Substituted;
}

FString FDialogueRules::PlainText(const FString& Text)
{
    FString Result;
    int32 Pos = 0;
    int32 Len = Text.Len();

    while (Pos < Len)
    {
        if (Text[Pos] == TEXT('['))
        {
            int32 TagStart = Pos;
            int32 CheckPos = Pos + 1;
            bool bClosing = false;
            if (CheckPos < Len && Text[CheckPos] == TEXT('/'))
            {
                bClosing = true;
                ++CheckPos;
            }

            // Check b, i, u
            if (CheckPos < Len)
            {
                TCHAR C = FChar::ToLower(Text[CheckPos]);
                if ((C == TEXT('b') || C == TEXT('i') || C == TEXT('u')) && CheckPos + 1 < Len && Text[CheckPos + 1] == TEXT(']'))
                {
                    Pos = CheckPos + 2;
                    continue;
                }
            }

            // Check colour or colour=...
            if (CheckPos + 6 <= Len && Text.Mid(CheckPos, 6).Compare(TEXT("colour"), ESearchCase::IgnoreCase) == 0)
            {
                int32 AfterColour = CheckPos + 6;
                if (AfterColour < Len && Text[AfterColour] == TEXT(']'))
                {
                    Pos = AfterColour + 1;
                    continue;
                }
                else if (!bClosing && AfterColour < Len && Text[AfterColour] == TEXT('='))
                {
                    int32 CloseBracket = Text.Find(TEXT("]"), ESearchCase::CaseSensitive, ESearchDir::FromStart, AfterColour + 1);
                    if (CloseBracket != INDEX_NONE)
                    {
                        Pos = CloseBracket + 1;
                        continue;
                    }
                }
            }
        }

        Result += Text[Pos];
        ++Pos;
    }

    return Result;
}

bool FDialogueRules::IsVariableAction(const FString& Type)
{
    return Type == TEXT("Affinity") || Type == TEXT("Inventory") || Type == TEXT("Set") || Type == TEXT("Toggle");
}

int32 FDialogueRules::ApplyAction(const FString& Type, int32 Before, int32 Amount)
{
    if (Type == TEXT("Set")) return Amount;
    if (Type == TEXT("Toggle")) return Before == 0 ? 1 : 0;
    return ApplyAmount(Type, Before, Amount);
}

int32 FDialogueRules::ApplyAmount(const FString& Type, int32 Before, int32 Amount)
{
    int64 Sum = (int64)Before + (int64)Amount;
    int64 MinVal = (Type == TEXT("Inventory")) ? 0 : (int64)TNumericLimits<int32>::Min();
    int64 MaxVal = (int64)TNumericLimits<int32>::Max();
    return (int32)FMath::Clamp(Sum, MinVal, MaxVal);
}

int32 FDialogueRules::ClampNumber(const FString& Key, int32 Value)
{
    return Key.StartsWith(TEXT("inv:")) ? FMath::Max(0, Value) : Value;
}

TArray<FString> FDialogueRules::SplitPages(const FString& Text)
{
    TArray<FString> Pages;
    Text.ParseIntoArrayLines(Pages, true);
    return Pages;
}

TArray<FString> FDialogueRules::BuildPages(const FString& Text, bool bSayItAll, uint64& InOutRandomState, int32& InOutRandomLine)
{
    TArray<FString> Pages = SplitPages(Text);
    if (Pages.Num() == 0)
    {
        return { NoDialogue };
    }
    if (bSayItAll)
    {
        return Pages;
    }
    if (InOutRandomLine < 0)
    {
        InOutRandomLine = SplitMix64(InOutRandomState, Pages.Num());
    }
    int32 Index = FMath::Min(InOutRandomLine, Pages.Num() - 1);
    return { Pages[Index] };
}

uint64 FDialogueRules::SeedState(int32 Seed)
{
    return (uint64)(int64)Seed;
}

int32 FDialogueRules::SplitMix64(uint64& InOutState, int32 Count)
{
    if (Count <= 0) return 0;
    uint64 Threshold = (0ULL - (uint64)Count) % (uint64)Count;
    uint64 Value = 0;
    do
    {
        InOutState += 0x9E3779B97F4A7C15ULL;
        Value = InOutState;
        Value = (Value ^ (Value >> 30)) * 0xBF58476D1CE4E5B9ULL;
        Value = (Value ^ (Value >> 27)) * 0x94D049BB133111EBULL;
        Value ^= Value >> 31;
    } while (Value < Threshold);
    return (int32)(Value % (uint64)Count);
}
