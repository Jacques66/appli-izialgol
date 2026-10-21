{ The target of a non-local goto is the activation found through the static
  chain: only the inner activations are left. }
program gotorec(output);
procedure p(n: integer);
label 1;
   procedure q(m: integer);
   begin if m = 0 then goto 1; q(m - 1) end;
begin
   if n > 0 then p(n - 1);
   q(3);
   writeln('not reached');
1: writeln('back in p(', n:1, ')')
end;
begin p(2) end.
