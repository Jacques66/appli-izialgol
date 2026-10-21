{ Non-local goto: a goto statement may lead out of a procedure to a label
  of an enclosing block. Here a recursive search of a maze abandons all the
  pending activations as soon as the exit is found. }
program maze(output);
label 99;
const size = 8;
type row = packed array[1..size] of char;
var m: array[1..size] of row;
    path: integer;
    r: integer;

procedure visit(i, j, depth: integer);
begin
   if (i >= 1) and (i <= size) then
      if (j >= 1) and (j <= size) then
         if m[i][j] = 'E' then
         begin
            path := depth;
            goto 99                    { leaves every active visit }
         end
         else if m[i][j] = ' ' then
         begin
            m[i][j] := '.';
            visit(i, j + 1, depth + 1);
            visit(i + 1, j, depth + 1);
            visit(i, j - 1, depth + 1);
            visit(i - 1, j, depth + 1);
            m[i][j] := ' '
         end
end;

begin
   m[1] := '# ######';
   m[2] := '#    # #';
   m[3] := '# ## # #';
   m[4] := '#  #   #';
   m[5] := '## ### #';
   m[6] := '#   #  #';
   m[7] := '# #   E#';
   m[8] := '########';
   path := 0;
   visit(1, 2, 0);
   writeln('no way out');
99:
   writeln('exit found after ', path:1, ' steps');
   for r := 1 to size do writeln(m[r])
end.
