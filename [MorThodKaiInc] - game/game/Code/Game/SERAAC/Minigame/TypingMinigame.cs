using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace SERAAC.Minigame
{
    /// <summary>
    /// Two-player typing challenge.
    /// P1 types letters (A-Z), while P2 types numbers (0-9).
    /// Both players must finish their sequence before the timer expires.
    /// </summary>
    public class TypingMinigame
    {
        private readonly Random _rng = new Random();
        private string _p1Sequence = string.Empty;
        private string _p2Sequence = string.Empty;
        private int _p1Index;
        private int _p2Index;
        private double _duration;
        private double _timer;
        private bool _active;
        private bool _evaluated;
        private bool _p1Complete;
        private bool _p2Complete;

        public bool Active => _active;
        public double Timer => _timer;
        public int SequenceLength => _p1Sequence.Length;
        public int P1Progress => _p1Index;
        public int P2Progress => _p2Index;
        public bool P1Complete => _p1Complete;
        public bool P2Complete => _p2Complete;
        public string P1Display => BuildDisplay(_p1Sequence, _p1Index);
        public string P2Display => BuildDisplay(_p2Sequence, _p2Index);

        public void Start(double duration = 5.0, int sequenceLength = 7)
        {
            _duration = Math.Max(0.5, duration);
            _timer = _duration;
            _p1Sequence = GenerateLetters(sequenceLength);
            _p2Sequence = GenerateNumbers(sequenceLength);
            _p1Index = 0;
            _p2Index = 0;
            _p1Complete = false;
            _p2Complete = false;
            _evaluated = false;
            _active = true;
        }

        public void Update(GameTime gameTime, KeyboardState current, KeyboardState previous)
        {
            if (!_active) return;

            _timer -= gameTime.ElapsedGameTime.TotalSeconds;
            if (_timer <= 0)
            {
                _timer = 0;
                _active = false;
                return;
            }

            // P1: alphabet only.
            if (!_p1Complete)
            {
                Keys expected = LetterToKey(_p1Sequence[_p1Index]);
                if (IsNewKeyPress(current, previous, expected))
                {
                    _p1Index++;
                    if (_p1Index >= _p1Sequence.Length)
                        _p1Complete = true;
                }
            }

            // P2: number row OR numpad.
            if (!_p2Complete)
            {
                char expected = _p2Sequence[_p2Index];
                if (IsNewNumberPress(current, previous, expected))
                {
                    _p2Index++;
                    if (_p2Index >= _p2Sequence.Length)
                        _p2Complete = true;
                }
            }

            if (_p1Complete && _p2Complete)
                _active = false;
        }

        public bool IsComplete()
        {
            return !_active && !_evaluated;
        }

        public bool Evaluate()
        {
            if (_evaluated) return _p1Complete && _p2Complete;
            _evaluated = true;
            return _p1Complete && _p2Complete;
        }

        private string GenerateLetters(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            char[] result = new char[Math.Max(1, length)];
            for (int i = 0; i < result.Length; i++)
                result[i] = chars[_rng.Next(chars.Length)];
            return new string(result);
        }

        private string GenerateNumbers(int length)
        {
            char[] result = new char[Math.Max(1, length)];
            for (int i = 0; i < result.Length; i++)
                result[i] = (char)('0' + _rng.Next(10));
            return new string(result);
        }

        private static string BuildDisplay(string sequence, int index)
        {
            if (string.IsNullOrEmpty(sequence)) return "";
            if (index >= sequence.Length) return sequence + "  ✓";
            return sequence.Substring(0, index) + "[" + sequence[index] + "]" + sequence.Substring(index + 1);
        }

        private static Keys LetterToKey(char c)
        {
            return (Keys)Enum.Parse(typeof(Keys), c.ToString());
        }

        private static bool IsNewKeyPress(KeyboardState current, KeyboardState previous, Keys key)
        {
            return current.IsKeyDown(key) && previous.IsKeyUp(key);
        }

        private static bool IsNewNumberPress(KeyboardState current, KeyboardState previous, char digit)
        {
            int n = digit - '0';
            Keys topRow = n == 0 ? Keys.D0 : (Keys)((int)Keys.D0 + n);
            Keys numPad = n == 0 ? Keys.NumPad0 : (Keys)((int)Keys.NumPad0 + n);

            return (current.IsKeyDown(topRow) && previous.IsKeyUp(topRow)) ||
                   (current.IsKeyDown(numPad) && previous.IsKeyUp(numPad));
        }
    }
}
