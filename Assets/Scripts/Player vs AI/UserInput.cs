
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
    InsideOfBoard(float __x, float __y)
    {
        return (__x >= 0) && (__x < 8)
            && (__y >= 0) && (__y < 8);
    }


    private IEnumerator
    MouseClickCoroutine()
    {
        int __x = -1, __y = -1;
        SquareSelected = -1;

        while (true)
        {
            yield return null;
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                // Left mouse button clicked

                // Get the current mouse position
                Vector3 mouse_pos = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());

                __x = (int)(mouse_pos.x + 0.5f);
                __y = (int)(mouse_pos.y + 0.5f);

                if (InsideOfBoard(__x, __y))
                    break;
            }
        }

        SquareSelected = 8 * __y + __x;
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

