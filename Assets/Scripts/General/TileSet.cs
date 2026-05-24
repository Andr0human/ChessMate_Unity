using UnityEngine;

public class TileSet : MonoBehaviour {

    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Color primary;
    [SerializeField] private Color highlightColor;
    [SerializeField] private Color moveStart;
    [SerializeField] private Color moveEnd;

    public void ResetBack(bool spareLastMove = false) {
        if (spareLastMove && (sr.color == moveEnd || sr.color == moveStart))
            return;
        sr.color = primary;
    }

    public void MarkStartMove() {
        sr.color = moveStart;
    }

    public void Highlight() {
        sr.color = highlightColor;
    }

    public void MarkEndMove() {
        sr.color = moveEnd;
    }

}
