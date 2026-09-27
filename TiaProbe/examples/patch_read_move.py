# -*- coding: utf-8 -*-
import re, io

path = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets\plc_blocks\Read.xml"
text = io.open(path, encoding="utf-8").read()

# 每个要插 MOVE 的网络：(网络序号, 源变量, IN索引)
# 参考第一段：MOVE 源变量 -> date.IN[IN索引]，ENO 串联 ADC
# PT 网络：PT{PT索引+1}；DT 网络：DT{DT索引}（DT01=DT[1]）
targets = [
    (2,  "DT01", 11),   # 网络02 IN[11]->DT[1] ADC_DT_DB_1
    (3,  "DT03", 13),   # 网络03 IN[13]->DT[3] ADC_DT_DB_3
    (4,  "DT04", 14),   # 网络04 IN[14]->DT[4] ADC_DT_DB_4
    (5,  "DT06", 16),   # 网络05 IN[16]->DT[6] ADC_DT_DB_6
    (9,  "PT04", 3),    # 网络09 IN[3]->PT[3] ADC_DB_3
    (10, "PT05", 4),    # 网络10 IN[4]->PT[4] ADC_DB_4
    (11, "PT07", 6),    # 网络11 IN[6]->PT[6] ADC_DB_6
    (12, "PT08", 7),    # 网络12 IN[7]->PT[7] ADC_DB_7
    (13, "PT10", 9),    # 网络13 IN[9]->PT[9] ADC_DB_9
    (14, "PT11", 10),   # 网络14 IN[10]->PT[10] ADC_DB_10
]

# 切分网络
units = re.findall(r'<SW\.Blocks\.CompileUnit ID="\d+" CompositionName="CompileUnits">.*?</SW\.Blocks\.CompileUnit>', text, re.S)
print("原网络数:", len(units))

def find_parts_blocks(u):
    """返回 (parts_open, parts_close, wires_open, wires_close) 的偏移"""
    i_parts = u.find("<Parts>")
    i_parts_end = u.find("</Parts>")
    i_wires = u.find("<Wires>")
    i_wires_end = u.find("</Wires>")
    return i_parts, i_parts_end, i_wires, i_wires_end

def max_uid(u):
    ids = [int(x) for x in re.findall(r'UId="(\d+)"', u)]
    return max(ids) if ids else 0

new_units = list(units)
for (netno, src, in_idx) in targets:
    u = units[netno-1]
    ip, ipe, iw, iwe = find_parts_blocks(u)
    if ip < 0 or iw < 0:
        print("网络%d 无 Parts/Wires，跳过" % netno); continue
    base = max_uid(u)
    uid_src = base+1; uid_in = base+2; uid_move = base+3
    uid_w1 = base+4; uid_w2 = base+5; uid_w3 = base+6; uid_w4 = base+7

    # 1) Parts 开头插入：源变量 Access、IN Access、Move Part
    add_parts = (
        '    <Access Scope="GlobalVariable" UId="%d">\n'
        '      <Symbol>\n'
        '        <Component Name="%s" />\n'
        '      </Symbol>\n'
        '    </Access>\n'
        '    <Access Scope="GlobalVariable" UId="%d">\n'
        '      <Symbol>\n'
        '        <Component Name="date" />\n'
        '        <Component Name="IN" AccessModifier="Array">\n'
        '          <Access Scope="LiteralConstant">\n'
        '            <Constant>\n'
        '              <ConstantType>DInt</ConstantType>\n'
        '              <ConstantValue>%d</ConstantValue>\n'
        '            </Constant>\n'
        '          </Access>\n'
        '        </Component>\n'
        '      </Symbol>\n'
        '    </Access>\n'
        '    <Part Name="Move" UId="%d" DisabledENO="true">\n'
        '      <TemplateValue Name="Card" Type="Cardinality">1</TemplateValue>\n'
        '    </Part>\n'
    ) % (uid_src, src, uid_in, in_idx, uid_move)

    # 2) Wires：替换 <Powerrail /> 到 ADC.en 的连线为 MOVE 串联
    # 原网络第一条 Wire 应为: <Powerrail /> <NameCon UId="ADC的UId" Name="en" />
    wbody = u[iw:iwe]
    m = re.search(r'<Wire UId="(\d+)">\s*<Powerrail />\s*<NameCon UId="(\d+)" Name="en" />\s*</Wire>', wbody)
    if not m:
        print("网络%d 未找到 Powerrail 连线，跳过" % netno); continue
    old_wire = m.group(0)
    adc_uid = int(m.group(2))
    new_wires = (
        '    <Wire UId="%d">\n      <Powerrail />\n      <NameCon UId="%d" Name="en" />\n    </Wire>\n'
        '    <Wire UId="%d">\n      <IdentCon UId="%d" />\n      <NameCon UId="%d" Name="in" />\n    </Wire>\n'
        '    <Wire UId="%d">\n      <NameCon UId="%d" Name="eno" />\n      <NameCon UId="%d" Name="en" />\n    </Wire>\n'
        '    <Wire UId="%d">\n      <NameCon UId="%d" Name="out1" />\n      <IdentCon UId="%d" />\n    </Wire>\n'
    ) % (uid_w1, uid_move, uid_w2, uid_src, uid_move, uid_w3, uid_move, adc_uid, uid_w4, uid_move, uid_in)

    wbody_new = wbody.replace(old_wire, new_wires, 1)
    u_new = u[:iw] + wbody_new + u[iwe:]
    # Access/Part 必须插在 <Parts> 标签之后（FlgNet 直接子元素只允许 Labels/Parts）
    u_new = u_new[:ip+7] + add_parts + u_new[ip+7:]
    new_units[netno-1] = u_new
    print("网络%d 已插入 MOVE: %s -> date.IN[%d] (ADC UId=%s)" % (netno, src, in_idx, adc_uid))

# 重写 Read.xml
out = text
# 替换原 units 段
for i, u in enumerate(units):
    out = out.replace(u, new_units[i], 1)

with io.open(path, "w", encoding="utf-8") as f:
    f.write(out)
print("写入完成: Read.xml，改动网络:", len(targets))
