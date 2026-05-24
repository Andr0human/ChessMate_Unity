
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;


class UserInput : MonoBehaviour
{
    public BoardHandler bh;

    public  int InitSquare;
    public  int DestSquare;
    public  int PromotedPiece;
    private int SquareSelected;


    private void
    ShowDestSquaresOnBoard(ref MoveList moves, int square)
    {
        bh.BoardReset(true);
        bh.BoardHighLight(moves.endIndex[square]);
    }


    private bool
    InsideOfBoard(float x, float y)
    {
        return (x >= 0) && (x < 8)
            && (y >= 0) && (y < 8);
    }


    private IEnumerator
    MouseClickCoroutine()
    {
        int x = -1, y = -1;
        SquareSelected = -1;

        while (true)
        {
            yield return null;
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                // Left mouse button clicked

                // Get the current mouse position
                Vector3 mousePos = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());

                x = (int)(mousePos.x + 0.5f);
                y = (int)(mousePos.y + 0.5f);

                if (InsideOfBoard(x, y))
                    break;
            }
        }

        SquareSelected = 8 * y + x;
    }


    public void
    GetSquares(ref MoveList movelist)
    {
        InitSquare = -1;
        DestSquare = -1;
        StartCoroutine( GetSquaresCoroutine(movelist) );
    }


    private IEnumerator
    GetSquaresCoroutine(MoveList movelist)
    {
        while (true)
        {
            yield return StartCoroutine( MouseClickCoroutine() );

            if (movelist.ValidInitSquare(SquareSelected))
            {
                if (SquareSelected == InitSquare)
                {
                    InitSquare = -1;
                    bh.BoardReset(true);
                }
                else
                {
                    InitSquare = SquareSelected;
                    ShowDestSquaresOnBoard(ref movelist, InitSquare);
                }
                continue;
            }

            if ((InitSquare != -1) && movelist.ValidDestSquare(InitSquare, SquareSelected))
            {
                DestSquare = SquareSelected;
                yield break;
            }
        }
    }
}

