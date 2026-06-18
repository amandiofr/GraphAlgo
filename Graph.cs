record GraphParam(string Name, int Min, int Max, int Default);

abstract class Graph
{
    public abstract string       Name   { get; }
    public abstract GraphParam[] Params { get; }
    public abstract PointF[]     Compute(int[] values, float width, float height, int n = 0, double offset = 0.0);
    public virtual  bool         IsDegenerate(int[] values) => false;
    public virtual  string       Formula(int[] v)           => "";
    public virtual  string       FormulaTemplate            => "";
}
