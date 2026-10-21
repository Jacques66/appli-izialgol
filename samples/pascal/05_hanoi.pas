{ Towers of Hanoi: recursive procedure with value parameters of an
  enumerated type. }
program hanoi(output);
type peg = (left, middle, right);
var moves: integer;

procedure name(p: peg);
begin
   case p of
      left:   write('left  ');
      middle: write('middle');
      right:  write('right ')
   end
end;

procedure move(n: integer; source, target, spare: peg);
begin
   if n > 0 then
   begin
      move(n - 1, source, spare, target);
      moves := moves + 1;
      write('move disk ', n:1, ' from ');
      name(source); write(' to '); name(target);
      writeln;
      move(n - 1, spare, target, source)
   end
end;

begin
   moves := 0;
   move(4, left, right, middle);
   writeln(moves:1, ' moves')
end.
