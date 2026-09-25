namespace SERAAC.Combat
{
    // The 3x3 tactical grid (cells 0-8, row-major, row 0 = front line facing the enemies).
    // The Vessel occupies one cell and can step to an adjacent cell each round instead of Defending.
    public static class CombatGrid
    {
        public const int Size = 3;
        public const int CellCount = Size * Size;
        public const int CenterCell = 4;

        public static (int row, int col) ToRowCol(int cell) => (cell / Size, cell % Size);
        public static int ToCell(int row, int col) => row * Size + col;

        public static bool TryStep(int fromCell, int deltaRow, int deltaCol, out int toCell)
        {
            var (row, col) = ToRowCol(fromCell);
            int newRow = row + deltaRow;
            int newCol = col + deltaCol;

            if (newRow < 0 || newRow >= Size || newCol < 0 || newCol >= Size)
            {
                toCell = fromCell;
                return false;
            }

            toCell = ToCell(newRow, newCol);
            return true;
        }
    }
}
